using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Nashi.Linux.Platform;
namespace Nashi.Linux;

internal static partial class SmokeTests
{
    private static string RenderWindow(Window window,string path)
    {
        window.UpdateLayout();
        var size=new PixelSize(Math.Max(1,(int)window.Bounds.Width),Math.Max(1,(int)window.Bounds.Height));
        using(var bmp=new RenderTargetBitmap(size,new Vector(96,96))){bmp.Render(window);bmp.Save(path,new PngBitmapEncoderOptions());}
        return $"{size.Width}x{size.Height}, {new FileInfo(path).Length} bytes";
    }
    static partial void SettingsScenes(List<(string Name,Scene Run)> scenes)
    {
        scenes.Add(("settings window",async(window,directory)=>
        {
            var settings=window.ShowSettings();
            await Task.Delay(600);
            var detail=RenderWindow(settings,Path.Combine(directory,"settings.png"));
            var general=settings.Section=="General";
            settings.Section="Formats";await Task.Delay(200);
            var switched=settings.Section=="Formats";
            settings.Close();await Task.Delay(100);
            return(general&&switched&&window.OpenSettings is null,detail);
        }));
        scenes.Add(("settings autostart",(window,directory)=>
        {
            var config=Path.Combine(directory,"xdg-config");
            var previous=Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME",config);
            try
            {
                if(Services.Autostart is not {} autostart)return Task.FromResult<(bool,string?)>((false,"Services.Autostart is null"));
                var file=Path.Combine(config,"autostart","nashi.desktop");
                if(autostart.Enabled)return Task.FromResult<(bool,string?)>((false,"enabled before Set(true)"));
                autostart.Set(true);
                var text=File.Exists(file)?File.ReadAllText(file):"";
                var expected="Exec="+Platform.Linux.Autostart.Quote(Environment.ProcessPath!)+" --background";
                var on=autostart.Enabled&&text.StartsWith("[Desktop Entry]")&&text.Contains("Type=Application")&&text.Contains(expected+"\n");
                autostart.Set(false);
                var off=!File.Exists(file)&&!autostart.Enabled;
                var escaped=Platform.Linux.Autostart.Quote("/a b/\"c\"$d`e\\f%g")=="\"/a b/\\\\\"c\\\\\"\\\\$d\\\\`e\\\\\\\\f%%g\"";
                return Task.FromResult<(bool,string?)>((on&&off&&escaped,$"on={on} off={off} escaped={escaped}"));
            }
            finally{Environment.SetEnvironmentVariable("XDG_CONFIG_HOME",previous);}
        }));
        scenes.Add(("tool setup window",async(window,directory)=>
        {
            var setup=window.ShowToolSetup();
            await Task.Delay(600);
            var detail=RenderWindow(setup,Path.Combine(directory,"tools.png"));
            var ok=!setup.Working&&setup.ContinueButton is not null;
            setup.Close();await Task.Delay(100);
            return(ok&&window.OpenToolSetup is null,detail);
        }));
    }
}
