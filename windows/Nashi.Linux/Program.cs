using Avalonia;
using Nashi.Linux.Platform;
namespace Nashi.Linux;

internal static class Program
{
    // Held for the life of the process by the primary instance; MainWindow.Desktop starts its listener.
    internal static SingleInstance? Instance{get;private set;}
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
        var instance=new SingleInstance();
        if(!instance.Primary)
        {
            // Relative paths mean nothing to the running instance, whose working directory differs.
            var forwarded=args.Where(a=>a=="--download-clipboard"||a=="--wheel"||(!a.StartsWith('-')&&(File.Exists(a)||Directory.Exists(a)))).Select(a=>a.StartsWith('-')?a:Path.GetFullPath(a)).ToList();
            // The wheel shortcut: this process still sees the Wayland clipboard, the running instance does not.
            if(args.Contains("--wheel"))forwarded.AddRange(Platform.Linux.CopiedFiles.Read());
            try{instance.Notify(forwarded.ToArray()).GetAwaiter().GetResult();instance.Dispose();return 0;}
            catch(Exception error)when(error is IOException or OperationCanceledException or System.Net.Sockets.SocketException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine("Nashi is already running but is still starting. Open it from the system tray or try again in a moment.");instance.Dispose();return 1;
            }
        }
        Instance=instance;
        // First launch by the shortcut: the copied files ride along as ordinary launch args and MainWindow opens the wheel once shown.
        if(args.Contains("--wheel"))args=args.Where(a=>a!="--wheel").Concat(Platform.Linux.CopiedFiles.Read()).Prepend("--wheel").ToArray();
        try{return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);}
        finally{instance.Dispose();}
    }
    internal static AppBuilder BuildAvaloniaApp()=>AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont();
}
