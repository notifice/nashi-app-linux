using Avalonia;
using Avalonia.Platform;
using Avalonia.Threading;
namespace Nashi.Linux.Platform.Linux;

internal sealed class SystemTheme:ISystemTheme
{
    private Action? changed;
    private bool hooked;
    public bool Dark=>Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant==PlatformThemeVariant.Dark;

    public event Action? Changed
    {
        add{changed+=value;Hook();}
        remove{changed-=value;}
    }

    // Services are created before the Application exists, so hook lazily.
    private void Hook()
    {
        if(hooked)return;
        var settings=Application.Current?.PlatformSettings;
        if(settings is null){Dispatcher.UIThread.Post(Hook);return;}
        hooked=true;
        settings.ColorValuesChanged+=(_,_)=>changed?.Invoke();
    }
}
