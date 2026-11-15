using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Nashi.Linux.Platform;
namespace Nashi.Linux;

// `Nashi --smoke-test` with NASHI_SMOKE_DIRECTORY set: shows the real window, renders it, checks the services, writes smoke.json.
internal static partial class SmokeTests
{
    // Later tasks register more scenes from their own files under Smoke/, one hook implementation each.
    internal delegate Task<(bool Ok,string? Detail)> Scene(MainWindow window,string directory);
    static partial void HomeScenes(List<(string Name,Scene Run)> scenes);
    static partial void WheelScenes(List<(string Name,Scene Run)> scenes);
    static partial void LibraryScenes(List<(string Name,Scene Run)> scenes);
    static partial void SettingsScenes(List<(string Name,Scene Run)> scenes);
    static partial void EditorScenes(List<(string Name,Scene Run)> scenes);
    static partial void IntegrationScenes(List<(string Name,Scene Run)> scenes);
    static partial void PreviewScenes(List<(string Name,Scene Run)> scenes);
    static partial void DesktopScenes(List<(string Name,Scene Run)> scenes);
    static partial void WebScenes(List<(string Name,Scene Run)> scenes);
    static partial void DocumentScenes(List<(string Name,Scene Run)> scenes);

    internal static int Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var results=new List<(string Name,bool Ok,string? Detail)>();
        Program.BuildAvaloniaApp().Start((app,_args)=>
        {
            using var cts=new CancellationTokenSource();
            Dispatcher.UIThread.Post(async()=>
            {
                try{await Execute(app,directory,results);}
                catch(Exception ex){results.Add(("smoke",false,ex.ToString()));}
                finally{cts.Cancel();}
            });
            _=Task.Delay(TimeSpan.FromSeconds(90)).ContinueWith(_=>{results.Add(("timeout",false,"smoke test exceeded 90s"));Dispatcher.UIThread.Post(cts.Cancel);});
            app.Run(cts.Token);
        },[]);
        var ok=results.Count>0&&results.All(r=>r.Ok);
        var json=new JsonObject{["ok"]=ok,["checks"]=new JsonArray(results.Select(r=>(JsonNode)new JsonObject{["name"]=r.Name,["ok"]=r.Ok,["detail"]=r.Detail}).ToArray())};
        File.WriteAllText(Path.Combine(directory,"smoke.json"),json.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(ok?"smoke test passed":"smoke test FAILED: "+string.Join("; ",results.Where(r=>!r.Ok).Select(r=>r.Name+" "+r.Detail)));
        return ok?0:1;
    }

    private static async Task Execute(Application app,string directory,List<(string Name,bool Ok,string? Detail)> results)
    {
        var window=(app.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow??new MainWindow([]);
        window.Show();
        await Task.Delay(800);
        window.UpdateLayout();
        try
        {
            var size=new PixelSize(Math.Max(1,(int)window.Bounds.Width),Math.Max(1,(int)window.Bounds.Height));
            var png=Path.Combine(directory,"home.png");
            using(var bmp=new RenderTargetBitmap(size,new Vector(96,96))){bmp.Render(window);bmp.Save(png,new PngBitmapEncoderOptions());}
            var len=new FileInfo(png).Length;
            results.Add(("render home.png",len>0,$"{size.Width}x{size.Height}, {len} bytes"));
        }
        catch(Exception ex){results.Add(("render home.png",false,ex.Message));}
        foreach(var (name,value) in new (string,object?)[]{("Files",Services.Files),("Dialogs",Services.Dialogs),("Shell",Services.Shell),("Screen",Services.Screen),("Theme",Services.Theme),("Drop",Services.Drop)})
            results.Add(("Services."+name,value is not null,null));
        var scenes=new List<(string Name,Scene Run)>();
        HomeScenes(scenes);WheelScenes(scenes);LibraryScenes(scenes);SettingsScenes(scenes);EditorScenes(scenes);IntegrationScenes(scenes);PreviewScenes(scenes);DesktopScenes(scenes);WebScenes(scenes);DocumentScenes(scenes);
        if(window is MainWindow main)
            foreach(var (name,run) in scenes)
            {
                try{var (ok,detail)=await run(main,directory);results.Add((name,ok,detail));}
                catch(Exception ex){results.Add((name,false,ex.GetType().Name+": "+ex.Message));}
            }
        else results.Add(("scenes",false,"main window is not MainWindow"));
        window.Close();
    }
}
