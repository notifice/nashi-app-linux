using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Nashi.Linux.Platform;
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
        Program.Instance?.Start(incoming=>Dispatcher.UIThread.Post(()=>AcceptLaunch(incoming)));
    }
    private void ShowMain(){Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();}
    private void Quit()
    {
        if(busy){ShowMain();L10n.SetText(status,"Finish or cancel the current operation before quitting.");return;}
        quitting=true;
        if(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)desktop.Shutdown();else Close();
    }
    // TODO(InitWeb): once the link download flow exists, ImportClipboardAsync should start it; until then it reports that downloads are unavailable.
    private void DownloadClipboard(){_=ImportClipboardAsync();}
    /// A later launch handed its arguments over: show the window, then open the files or import the clipboard.
    internal void AcceptLaunch(string[] args)
    {
        ShowMain();Console.Error.WriteLine("Nashi: forwarded launch: "+string.Join(" ",args));
        if(args.Contains("--download-clipboard")){DownloadClipboard();return;}
        var paths=args.Where(a=>!a.StartsWith('-')).ToArray();
        if(paths.Length==0||busy)return;
        files.Clear();AddFiles(paths);ShowLibrary(false);
    }
}
