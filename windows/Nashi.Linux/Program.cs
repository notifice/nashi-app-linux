using Avalonia;
using Nashi.Linux.Platform;
namespace Nashi.Linux;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Services.Initialize();
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    internal static AppBuilder BuildAvaloniaApp()=>AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont();
}
