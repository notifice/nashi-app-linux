using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Nashi.Engine;
namespace Nashi.Linux;

internal static partial class SmokeTests
{
    private static void PressKey(WheelWindow wheel,Key key)=>wheel.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=key,Source=wheel});
    // Conversion wheel for a PNG: arrow keys and Enter pick jpg, Escape returns nothing, Ctrl switches to the tools wheel, and the wheel renders to wheel.png.
    static partial void WheelScenes(List<(string Name,Scene Run)> scenes)=>scenes.Add(("wheel picks jpg for png",async(window,directory)=>
    {
        var input=Path.Combine(directory,"fixture.png");
        if(!File.Exists(input))File.WriteAllBytes(input,ImageEngine.PngBytes(new PixelImage(64,48,Enumerable.Repeat((byte)200,64*48*4).ToArray())));
        var previous=Feedback.Enabled;Feedback.Enabled=false;WheelWindow.KeepOpenOnDeactivateForTest=true;
        try
        {
            var files=new[]{input};var convert=WheelPresentation.Choices(files,"convert");var index=convert.ToList().FindIndex(c=>c.Id=="convert:jpg");
            if(index<0)return(false,"no jpg segment for png: "+string.Join(",",convert.Select(c=>c.Id)));
            var wheel=new WheelWindow(window,convert,modes:m=>WheelPresentation.Choices(files,m),initialMode:"convert");
            var task=wheel.ShowAsync();await Task.Delay(500);wheel.UpdateLayout();
            if(wheel.SelectedIndex!=0)return(false,"initial highlight is "+wheel.SelectedIndex);
            var size=new PixelSize(Math.Max(1,(int)wheel.Bounds.Width),Math.Max(1,(int)wheel.Bounds.Height));
            using(var bmp=new RenderTargetBitmap(size,new Vector(96,96))){bmp.Render(wheel);bmp.Save(Path.Combine(directory,"wheel.png"),new PngBitmapEncoderOptions());}
            for(var i=0;i<index;i++)PressKey(wheel,Key.Right);
            if(wheel.SelectedIndex!=index)return(false,$"arrow keys highlighted {wheel.SelectedIndex}, wanted {index}");
            PressKey(wheel,Key.Enter);
            var picked=await task.WaitAsync(TimeSpan.FromSeconds(5));
            if(picked?.Id!="convert:jpg")return(false,"picked "+picked?.Id);
            var escape=new WheelWindow(window,convert);var escaped=escape.ShowAsync();await Task.Delay(200);PressKey(escape,Key.Escape);
            if(await escaped.WaitAsync(TimeSpan.FromSeconds(5)) is not null)return(false,"Escape returned a choice");
            var arrows=new WheelWindow(window,convert);var arrowed=arrows.ShowAsync();await Task.Delay(200);PressKey(arrows,Key.Right);PressKey(arrows,Key.Enter);
            if((await arrowed.WaitAsync(TimeSpan.FromSeconds(5)))?.Id!=convert[1].Id)return(false,"Right then Enter did not pick the second segment");
            var tools=new WheelWindow(window,convert,modes:m=>WheelPresentation.Choices(files,m),initialMode:"convert");var toolsTask=tools.ShowAsync();await Task.Delay(200);
            PressKey(tools,Key.LeftCtrl);var mode=tools.Mode;tools.Close();await toolsTask;
            return(mode=="tools",$"picked {picked.Id} at index {index} of {convert.Count}; Ctrl switched to {mode}");
        }
        finally{Feedback.Enabled=previous;WheelWindow.KeepOpenOnDeactivateForTest=false;}
    }));
}
