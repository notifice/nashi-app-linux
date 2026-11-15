using Avalonia;
using Nashi.Linux.Platform;
namespace Nashi.Linux;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Services.Initialize();
        if(args.Contains("--smoke-test"))
        {
            var directory=Environment.GetEnvironmentVariable("NASHI_SMOKE_DIRECTORY");
            if(string.IsNullOrWhiteSpace(directory)){Console.Error.WriteLine("NASHI_SMOKE_DIRECTORY is required for --smoke-test");return 1;}
            return SmokeTests.Run(directory);
        }
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    internal static AppBuilder BuildAvaloniaApp()=>AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont();
}
