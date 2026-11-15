using Nashi.Engine;
namespace Nashi.Linux;

internal static partial class SmokeTests
{
    // Phase 1 acceptance: the same conversion path the Convert button uses, PNG to JPG beside the source.
    static partial void HomeScenes(List<(string Name,Scene Run)> scenes)=>scenes.Add(("home convert png to jpg",async(window,directory)=>
    {
        var input=Path.Combine(directory,"fixture.png");
        if(!File.Exists(input))File.WriteAllBytes(input,ImageEngine.PngBytes(new PixelImage(64,48,Enumerable.Repeat((byte)200,64*48*4).ToArray())));
        window.AddFiles([input]);
        if(window.SelectedFiles.Count!=1)return(false,"file not added");
        var outputs=await window.ConvertAsync("jpg");
        var ok=outputs.Count==1&&File.Exists(outputs[0])&&Path.GetExtension(outputs[0])==".jpg"&&ImageEngine.Load(outputs[0]) is {Width:64,Height:48};
        return(ok,string.Join(", ",outputs));
    }));
}
