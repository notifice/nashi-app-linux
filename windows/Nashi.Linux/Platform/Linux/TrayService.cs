using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
namespace Nashi.Linux.Platform.Linux;

// StatusNotifierItem tray icon through Avalonia's TrayIcon. Without a StatusNotifier host (GNOME without the AppIndicator extension, a bare WM) the
// icon would be invisible, so Available is false and the app never hides itself.
internal sealed class TrayService:ITrayService
{
    private TrayIcon? icon;private Action? open;private Func<IReadOnlyList<(string Title,Action Run)>>? menu;private string? update;
    private readonly Lazy<bool> available=new(HostPresent);
    public bool Available=>available.Value;
    private static bool HostPresent()
    {
        try
        {
            var info=new ProcessStartInfo("gdbus"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
            foreach(var argument in new[]{"call","--session","--dest","org.freedesktop.DBus","--object-path","/org/freedesktop/DBus","--method","org.freedesktop.DBus.NameHasOwner","org.kde.StatusNotifierWatcher"})info.ArgumentList.Add(argument);
            using var process=Process.Start(info);
            if(process is null)return false;
            var output=process.StandardOutput.ReadToEndAsync();
            if(!process.WaitForExit(2000)){try{process.Kill();}catch(InvalidOperationException){}return false;}
            return process.ExitCode==0&&output.GetAwaiter().GetResult().Contains("true");
        }
        catch(Exception error)when(error is System.ComponentModel.Win32Exception or InvalidOperationException){return false;}
    }
    public void Install(Action open,Func<IReadOnlyList<(string Title,Action Run)>> menu)
    {
        if(!Available||Application.Current is not {} app)return;
        this.open=open;this.menu=menu;
        icon??=Create(app);Rebuild();
    }
    private TrayIcon Create(Application app)
    {
        var created=new TrayIcon{Icon=new WindowIcon(AssetLoader.Open(new Uri("avares://Nashi/Assets/NashiIcon.png"))),ToolTipText="Nashi",IsVisible=true};
        created.Clicked+=(_,_)=>open?.Invoke();
        TrayIcon.SetIcons(app,[created]);return created;
    }
    private void Rebuild()
    {
        if(icon is null)return;
        var native=new NativeMenu();
        void Item(string title,Action run){var item=new NativeMenuItem(title);item.Click+=(_,_)=>run();native.Add(item);}
        Item("Open Nashi",()=>open?.Invoke());
        if(update is not null)Item("Update to "+update,()=>open?.Invoke());
        foreach(var (title,run) in menu?.Invoke()??[])Item(title,run);
        icon.Menu=native;
        icon.ToolTipText=update is null?"Nashi":"Nashi - update "+update+" available";
    }
    public void UpdateAvailable(string version){update=version;Rebuild();}
    public void Dispose(){if(icon is not null){icon.IsVisible=false;icon.Dispose();icon=null;}}
}
