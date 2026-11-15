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
        scenes.Add(("tray availability",(window,directory)=>
            Task.FromResult<(bool Ok,string? Detail)>((Services.Tray is not null,Services.Tray is null?"no tray service":"StatusNotifier host available: "+Services.Tray.Available))));
    }
}
