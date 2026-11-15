using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Nashi.Linux.Platform;
using Nashi.Linux.Platform.Linux;
namespace Nashi.Linux;

// Tray, keep-in-tray close behaviour and launches forwarded by SingleInstance (the Linux counterpart of SetupDesktop and AcceptLaunch in Nashi.Windows).
internal sealed partial class MainWindow
{
    private bool quitting,trayInstalled;
    partial void InitDesktop()
    {
        if(Services.Tray is {Available:true} tray)
        {
            tray.Install(ShowMain,()=>
            [
                ("Library",()=>{ShowMain();ShowLibrary(true);}),
                ("Download copied link",()=>{ShowMain();DownloadClipboard();}),
                ("Settings",()=>{ShowMain();ShowSettings();}),
                ("Quit Nashi",Quit)
            ]);
            trayInstalled=true;
        }
        Closing+=(_,e)=>{if(!e.Cancel&&trayInstalled&&preferences.Value.KeepInTray&&!quitting){e.Cancel=true;Hide();}};
        Closed+=(_,_)=>Services.Tray?.Dispose();
        // Without a tray icon a hidden Nashi could not be reached, so --background then shows the window.
        if(trayInstalled&&launchArgs.Contains("--background"))Opened+=(_,_)=>Hide();
        // A first launch by the wheel shortcut never shows this window (App leaves it unshown); the wheel opens at the pointer instead.
        if(launchArgs.Contains("--wheel"))Dispatcher.UIThread.Post(()=>AcceptLaunch(launchArgs));
        Opened+=(_,_)=>everShown=true;
        var focusWatch=WindowOpenedEvent.AddClassHandler<Window>((window,args)=>{if(!Shown&&window.ShowActivated&&(wheelLaunching||window.Owner is not null&&window.Owner==anchor))_=EnsureActiveAsync(window);});Closed+=(_,_)=>focusWatch.Dispose();
        Program.Instance?.Start(incoming=>Dispatcher.UIThread.Post(()=>AcceptLaunch(incoming)));
    }
    private void ShowMain(){Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();}
    private void Quit()
    {
        if(busy){ShowMain();L10n.SetText(status,"Finish or cancel the current operation before quitting.");return;}
        quitting=true;
        if(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)desktop.Shutdown();else Close();
    }
    private void DownloadClipboard(){_=OpenCopiedLinkAsync();}
    /// A later launch handed its arguments over: show the window, then open the files or import the clipboard. The wheel shortcut (--wheel) leaves the window alone.
    internal void AcceptLaunch(string[] args)
    {
        Console.Error.WriteLine("Nashi: forwarded launch: "+string.Join(" ",args));
        var paths=args.Where(a=>!a.StartsWith('-')).ToArray();
        if(args.Contains("--wheel")){_=WheelLaunchAsync(paths);return;}
        ShowMain();
        if(args.Contains("--download-clipboard")){DownloadClipboard();return;}
        if(paths.Length==0||busy)return;
        files.Clear();AddFiles(paths);ShowLibrary(false);
    }
    // Set while a shortcut launch runs: the wheel, a second (Convert) wheel and toasts go to the pointer.
    private ScreenSpot? wheelSpot;private Window? anchor;private bool wheelLaunching,everShown;
    internal ScreenSpot? WheelSpotForTest=>wheelSpot;internal bool AnchorOpenForTest=>anchor is not null;
    /// Replaces the pointer lookup in smoke runs, which have no KWin.
    internal static Func<Task<PointerLocation.Report?>>? PointerForTest;
    private bool Shown=>IsVisible&&WindowState!=WindowState.Minimized;
    /// Owner for editors and dialogs. While this window is hidden (shortcut launch) Avalonia cannot own a dialog by it, so a one-pixel transparent window
    /// at the centre of the pointer's screen stands in: CenterOwner editors then open centred on that screen, without showing Nashi.
    internal Window DialogOwner
    {
        get
        {
            if(Shown)return this;if(anchor is not null)return anchor;
            var area=wheelSpot?.WorkingArea??Screens.Primary?.WorkingArea??new PixelRect(0,0,1,1);
            var stand=new Window{Title="Nashi",Width=1,Height=1,CanResize=false,ShowInTaskbar=false,ShowActivated=false,WindowDecorations=WindowDecorations.None,Background=Avalonia.Media.Brushes.Transparent,TransparencyLevelHint=[WindowTransparencyLevel.Transparent],WindowStartupLocation=WindowStartupLocation.Manual,Position=new(area.X+area.Width/2,area.Y+area.Height/2),Icon=Icon};
            stand.Closed+=(_,_)=>{if(anchor==stand)anchor=null;};anchor=stand;stand.Show();return stand;
        }
    }
    /// A progress toast at the bottom of this window, or at the pointer while it is hidden.
    private ToastWindow OpenToast(Action? cancel){var toast=new ToastWindow(Shown?this:null,cancel,wheelSpot);if(Shown)toast.Show(this);else toast.Show();return toast;}
    /// The global shortcut: the copied files (or none) arrive here; the wheel opens on the pointer and Nashi's own window is neither shown nor raised.
    private async Task WheelLaunchAsync(string[] paths)
    {
        if(wheelLaunching){WheelWindow.Current?.Activate();return;}
        wheelLaunching=true;
        if(!everShown&&Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime{MainWindow:null} hidden)hidden.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        try
        {
            wheelSpot=await LocatePointerAsync();
            if(busy){ShowNotice("Nashi is still working on the last task.");return;}
            if(paths.Length==0){L10n.SetText(status,"Copy files in your file manager (Ctrl+C), then press the Nashi shortcut.");ShowNotice("Copy files in your file manager (Ctrl+C), then press the Nashi shortcut.");return;}
            files.Clear();AddFiles(paths);ShowLibrary(false);
            await ShowWheelAsync();
        }
        catch(Exception error){await ShowInfoAsync(error.Message,"Couldn’t open this tool");}
        finally{wheelSpot=null;wheelLaunching=false;anchor?.Close();ExitIfUnreachable();}
    }
    private async Task<ScreenSpot> LocatePointerAsync()
    {
        var report=PointerForTest is {} test?await test():await PointerLocation.QueryAsync(TimeSpan.FromMilliseconds(500));
        var spot=PointerLocation.Spot(report,Screens.All,Screens.Primary);
        Console.Error.WriteLine($"Nashi: wheel at {spot.Center.X:0.#},{spot.Center.Y:0.#} (KWin {(report is {} r?$"{r.Cursor.X},{r.Cursor.Y} on {r.Outputs.Count} outputs":"unavailable")}; screens {string.Join(" ",Screens.All.Select(s=>$"{s.DisplayName}={s.Bounds}@{s.Scaling}"))})");
        return spot;
    }
    // The wheel and the editors it opens need the keyboard (arrows, Esc, Enter) although Nashi had no focus when the shortcut fired.
    private static async Task EnsureActiveAsync(Window window)
    {
        await Task.Delay(250);if(window.IsActive||!window.IsVisible)return;
        window.Activate();await Task.Delay(150);if(window.IsActive||!window.IsVisible||PointerForTest is not null)return;
        var raised=await KWinScript.ActivateAsync(window.Title??"",TimeSpan.FromMilliseconds(500));Console.Error.WriteLine($"Nashi: focused \"{window.Title}\" through KWin: {raised}");
    }
    private void ShowNotice(string message){var toast=new ToastWindow(null,null,wheelSpot);toast.Show();toast.Notice(message);}
    /// Without a tray icon a Nashi that was started hidden by the shortcut and never shown could not be reached, so it quits once its windows (toasts, Float shelves) close.
    private void ExitIfUnreachable()
    {
        if(trayInstalled||everShown||Program.Instance is null||Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop||desktop.MainWindow is not null)return;
        if(desktop.Windows.Count==0)desktop.Shutdown();else desktop.ShutdownMode=ShutdownMode.OnLastWindowClose;
    }
}
