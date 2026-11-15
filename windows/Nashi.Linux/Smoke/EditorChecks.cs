using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Nashi.Engine;
using Nashi.Linux.Platform;
namespace Nashi.Linux;

// P2-G: the image tool editor, driven through the same ShowDetailedAsync path MainWindow will use.
internal static partial class SmokeTests
{
    static partial void EditorScenes(List<(string Name,Scene Run)> scenes)
    {
        scenes.Add(("editor crop",async(window,directory)=>
        {
            var file=EditorFixture(directory);var (editor,result)=await OpenEditor(window,"crop",file);
            // Dragging outside any selection starts one; Shift locks a square.
            editor.DragForTest(new(20,10),new(120,60));
            var dragged=editor.FieldForTest("X").Text=="20"&&editor.FieldForTest("Y").Text=="10"&&editor.FieldForTest("Width").Text=="100"&&editor.FieldForTest("Height").Text=="50";
            editor.DragForTest(new(5,5),new(105,35),shift:true);
            var square=editor.FieldForTest("Width").Text==editor.FieldForTest("Height").Text&&editor.FieldForTest("Width").Text=="30";
            editor.SelectAspectForTest("16:9");
            var preset=double.Parse(editor.FieldForTest("Width").Text!)/double.Parse(editor.FieldForTest("Height").Text!) is >1.7 and <1.85;
            editor.SelectAspectForTest("Free");
            editor.SetCropForTest(40,20,100,50);
            await Task.Delay(300);SaveRender(editor,Path.Combine(directory,"crop.png"));
            editor.PressForTest(Key.Enter);
            var accepted=await result;
            if(accepted is null)return(false,"Enter did not accept");
            if(accepted.Image.Crop!=new Nashi.Engine.PixelRect(40,20,100,50))return(false,"crop "+accepted.Image.Crop);
            var size=ApplyAndMeasure("crop",file,Path.Combine(directory,"crop-out"),accepted.Image);
            return(dragged&&square&&preset&&size==(100,50),$"drag={dragged} square={square} preset={preset} output={size}");
        }));
        scenes.Add(("editor photo rotate",async(window,directory)=>
        {
            var file=EditorFixture(directory);var (editor,result)=await OpenEditor(window,"editImage",file);
            editor.RotatePhotoForTest(90);editor.FieldForTest("Exposure").Text="0.5";
            await editor.WaitForPreviewForTest();await Task.Delay(500);await editor.WaitForPreviewForTest();
            SaveRender(editor,Path.Combine(directory,"photo.png"));
            editor.PressForTest(Key.Enter);
            var accepted=await result;
            if(accepted is null)return(false,"Enter did not accept");
            if(accepted.Image.Rotation!=90||accepted.Image.Adjustments.Exposure!=.5)return(false,$"rotation {accepted.Image.Rotation} exposure {accepted.Image.Adjustments.Exposure}");
            var size=ApplyAndMeasure("editImage",file,Path.Combine(directory,"photo-out"),accepted.Image);
            return(size==(100,200),"output "+size);
        }));
        scenes.Add(("editor resize and cancel",async(window,directory)=>
        {
            var file=EditorFixture(directory);
            var (cancelled,cancelResult)=await OpenEditor(window,"resize",file);
            cancelled.PressForTest(Key.Escape);
            if(await cancelResult is not null)return(false,"Escape returned options");
            var (editor,result)=await OpenEditor(window,"resize",file);
            editor.FieldForTest("Width").Text="100";editor.FieldForTest("Height").Text="50";
            editor.PressForTest(Key.Enter);
            var accepted=await result;
            if(accepted is null)return(false,"Enter did not accept");
            var size=ApplyAndMeasure("resize",file,Path.Combine(directory,"resize-out"),accepted.Image);
            return(size==(100,50),"output "+size);
        }));
    }

    private static string EditorFixture(string directory)
    {
        var file=Path.Combine(directory,"editor-fixture.png");
        if(File.Exists(file))return file;
        var pixels=new byte[200*100*4];
        for(var y=0;y<100;y++)for(var x=0;x<200;x++){var i=(y*200+x)*4;pixels[i]=(byte)(x*255/199);pixels[i+1]=(byte)(y*255/99);pixels[i+2]=(byte)(255-x*255/199);pixels[i+3]=255;}
        File.WriteAllBytes(file,ImageEngine.PngBytes(new PixelImage(200,100,pixels)));
        return file;
    }
    private static async Task<(ToolEditor Editor,Task<ToolEditorResult?> Result)> OpenEditor(MainWindow window,string tool,string file)
    {
        var result=ToolEditor.ShowDetailedAsync(window,tool,[file]);
        for(var n=0;n<100&&ToolEditor.Active is null;n++)await Task.Delay(50);
        var editor=ToolEditor.Active??throw new InvalidOperationException("editor did not open");
        await Task.Delay(600);editor.UpdateLayout();
        return(editor,result);
    }
    private static void SaveRender(ToolEditor editor,string path)
    {
        using var bitmap=Bitmaps.FromControl(editor);bitmap.Save(path,new PngBitmapEncoderOptions());
    }
    private static (int,int) ApplyAndMeasure(string tool,string file,string destination,ImageOptions options)
    {
        Directory.CreateDirectory(destination);
        var outputs=ImageEngine.Apply(tool,[file],destination,options,"png");
        var image=ImageEngine.Load(outputs.Single());return(image.Width,image.Height);
    }
}
