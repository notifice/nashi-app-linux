using Nashi.Linux.Platform;
namespace Nashi.Linux;

internal static partial class SmokeTests
{
    static partial void DesktopScenes(List<(string Name,Scene Run)> scenes)
    {
        scenes.Add(("single instance forwarding",async(window,directory)=>
        {
            // A unique name keeps this away from a real running Nashi.
            var name="nashi-smoke-"+Guid.NewGuid().ToString("N");
            var received=new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var first=new SingleInstance(name);
            if(!first.Primary)return(false,"first instance was not primary");
            first.Start(args=>received.TrySetResult(args));
            using(var second=new SingleInstance(name))
            {
                if(second.Primary)return(false,"second instance became primary");
                await second.Notify(["/tmp/a b.png","--download-clipboard"]);
            }
            var got=await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var forwarded=got.Length==2&&got[0]=="/tmp/a b.png"&&got[1]=="--download-clipboard";
            first.Dispose();
            using var third=new SingleInstance(name);
            return(forwarded&&third.Primary,$"forwarded {got.Length} args, primary again after dispose: {third.Primary}");
        }));
        scenes.Add(("copied files uri parsing",(window,directory)=>
        {
            var dir=Path.Combine(directory,"copied files");Directory.CreateDirectory(dir);
            var a=Path.Combine(dir,"a b.png");var folder=Path.Combine(dir,"sub");File.WriteAllText(a,"x");Directory.CreateDirectory(folder);
            var uri=new Uri(a).AbsoluteUri;var folderUri=new Uri(folder).AbsoluteUri;
            var text=$"# comment\r\n{uri}\r\n\r\nhttps://example.com/x.png\r\n{new Uri(Path.Combine(dir,"missing.png")).AbsoluteUri}\r\n{uri}\r\n{folderUri}\r\n";
            var plain=Platform.Linux.CopiedFiles.Parse(text,false);var gnome=Platform.Linux.CopiedFiles.Parse("copy\n"+uri+"\n",true);var cut=Platform.Linux.CopiedFiles.Parse("cut\n"+folderUri,true);
            var ok=plain.Count==2&&plain[0]==a&&plain[1]==folder&&gnome.Count==1&&gnome[0]==a&&cut.Count==1&&uri.Contains("%20");
            return Task.FromResult<(bool Ok,string? Detail)>((ok,$"plain {plain.Count}, gnome {gnome.Count}, cut {cut.Count}, uri {uri}"));
        }));
        scenes.Add(("pointer mapping for the wheel shortcut",(window,directory)=>
        {
            // The user's KDE: DP-1 at 1.7 (KWin 1080,380 2259x1271, X 3840x2160+1836+646) and DP-2 at 1 (KWin 1080x1920, X 1836x3264+0+0): XWayland scales everything by 1.7.
            var screens=new List<(string? Name,Avalonia.PixelRect Bounds)>{("DP-1",new(1836,646,3840,2160)),("DP-2",new(0,0,1836,3264))};
            var unnamed=screens.Select(s=>((string?)null,s.Bounds)).ToList();var system=new List<(string? Name,Avalonia.PixelRect Bounds)>{("DP-1",new(1080,380,2259,1271)),("DP-2",new(0,0,1080,1920))};
            var outputs=";DP-1,1080,380,2259,1271;DP-2,0,0,1080,1920";
            static bool Near((Avalonia.Point Point,int Screen)? got,double x,double y,int screen)=>got is var (p,i)&&i==screen&&Math.Abs(p.X-x)<0.5&&Math.Abs(p.Y-y)<0.5;
            var dp1=Platform.Linux.PointerLocation.Map(Platform.Linux.PointerLocation.Parse("1245,997"+outputs)!.Value,screens);
            var dp2=Platform.Linux.PointerLocation.Map(Platform.Linux.PointerLocation.Parse("500,500"+outputs)!.Value,screens);
            var byOrder=Platform.Linux.PointerLocation.Map(Platform.Linux.PointerLocation.Parse("1245,997"+outputs)!.Value,unnamed);
            var scaledBySystem=Platform.Linux.PointerLocation.Map(Platform.Linux.PointerLocation.Parse("1245,997"+outputs)!.Value,system);
            var plasma5=Platform.Linux.PointerLocation.Map(Platform.Linux.PointerLocation.Parse("700,700")!.Value,system);
            var garbage=Platform.Linux.PointerLocation.Parse("error:ReferenceError")is null&&Platform.Linux.PointerLocation.Parse("")is null;
            var middle=new Platform.Linux.ScreenSpot(new(500,500),new(0,0,1000,1000),2).Place(new(100,100));var corner=new Platform.Linux.ScreenSpot(new(10,990),new(0,0,1000,1000),2).Place(new(100,100));
            var ok=Near(dp1,2116.5,1694.9,0)&&Near(dp2,850,850,1)&&Near(byOrder,2116.5,1694.9,0)&&Near(scaledBySystem,1245,997,0)&&Near(plasma5,700,700,1)&&garbage&&middle==new Avalonia.PixelPoint(400,400)&&corner==new Avalonia.PixelPoint(0,800);
            return Task.FromResult<(bool Ok,string? Detail)>((ok,$"DP-1 {dp1}, DP-2 {dp2}, by order {byOrder}, system {scaledBySystem}, plasma5 {plasma5}, garbage rejected {garbage}, place {middle} {corner}"));
        }));
        scenes.Add(("wheel launch shortcut",async(window,directory)=>
        {
            var input=Path.Combine(directory,"wheel-launch.png");
            File.WriteAllBytes(input,Nashi.Engine.ImageEngine.PngBytes(new Nashi.Engine.PixelImage(32,32,Enumerable.Repeat((byte)200,32*32*4).ToArray())));
            var previous=Feedback.Enabled;Feedback.Enabled=false;WheelWindow.KeepOpenOnDeactivateForTest=true;
            // A pretend KWin at half the X resolution with the pointer in the middle of the primary screen: the wheel must be centred there and Nashi stay hidden.
            var primary=window.Screens.Primary??window.Screens.All[0];var b=primary.Bounds;
            var report=new Platform.Linux.PointerLocation.Report(new(b.X/2d+b.Width/4d,b.Y/2d+b.Height/4d),[new("smoke",new(b.X/2d,b.Y/2d,b.Width/2d,b.Height/2d))]);
            MainWindow.PointerForTest=()=>Task.FromResult<Platform.Linux.PointerLocation.Report?>(report);
            try
            {
                window.ClearFilesForTest();window.Hide();await Task.Delay(200);
                window.AcceptLaunch(["--wheel",input]);await Task.Delay(700);
                var wheel=WheelWindow.Current;var loaded=window.SelectedFiles.Count==1&&window.SelectedFiles[0]==input;var hidden=!window.IsVisible;
                if(wheel is null)return(false,"AcceptLaunch --wheel with a file opened no wheel");
                var spot=window.WheelSpotForTest;var expected=spot?.Place(new(wheel.Width,wheel.Height));
                var centred=window.Screens.All.Count!=1||spot is {} s&&Math.Abs(s.Center.X-(b.X+b.Width/2d))<1&&Math.Abs(s.Center.Y-(b.Y+b.Height/2d))<1;
                var placed=expected is {} e&&Math.Abs(wheel.Position.X-e.X)<=1&&Math.Abs(wheel.Position.Y-e.Y)<=1&&wheel.Topmost&&wheel.Owner is null;
                var anchor=window.DialogOwner;var stands=anchor!=window&&anchor.IsVisible&&window.AnchorOpenForTest;
                wheel.Close();await Task.Delay(300);var cleaned=!window.AnchorOpenForTest&&window.WheelSpotForTest is null;
                window.ClearFilesForTest();
                window.AcceptLaunch(["--wheel"]);await Task.Delay(500);
                var none=WheelWindow.Current is null;var message=window.StatusForTest;var stillHidden=!window.IsVisible;
                return(loaded&&hidden&&centred&&placed&&stands&&cleaned&&none&&stillHidden&&message.Contains("Copy files in your file manager"),$"loaded {loaded}, hidden {hidden}, spot {spot}, wheel at {wheel.Position} wanted {expected}, topmost {wheel.Topmost}, stand-in owner {stands}, cleaned {cleaned}, no wheel when empty {none}, hidden after empty {stillHidden}, status \"{message}\"");
            }
            finally{MainWindow.PointerForTest=null;Feedback.Enabled=previous;WheelWindow.KeepOpenOnDeactivateForTest=false;WheelWindow.Current?.Close();window.Show();await Task.Delay(200);}
        }));
        scenes.Add(("tray availability",(window,directory)=>
            Task.FromResult<(bool Ok,string? Detail)>((Services.Tray is not null,Services.Tray is null?"no tray service":"StatusNotifier host available: "+Services.Tray.Available))));
    }
}
