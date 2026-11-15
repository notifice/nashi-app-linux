using Avalonia;
using Avalonia.Input;
using Nashi.Engine;
namespace Nashi.Linux;

// P5: frame, collage, redact and annotate editors, driven through ShowDetailedAsync and the editor test seams, then run through ImageEngine.Apply.
internal static partial class SmokeTests
{
    static partial void ImageEditorScenes(List<(string Name,Scene Run)> scenes)
    {
        scenes.Add(("editor frame",async(window,directory)=>
        {
            var file=EditorFixture(directory);var (editor,result)=await OpenEditor(window,"frameImage",file);
            editor.FieldForTest("Padding").Text="40";editor.FieldForTest("Corner radius").Text="0";editor.FieldForTest("Shadow radius").Text="0";
            await Task.Delay(500);await editor.WaitForPreviewForTest();
            var preview=editor.PreviewSizeForTest;
            SaveRender(editor,Path.Combine(directory,"frame.png"));
            editor.PressForTest(Key.Enter);
            var accepted=await result;
            if(accepted is null)return(false,"Enter did not accept");
            if(accepted.Image.Frame.Padding!=40)return(false,"padding "+accepted.Image.Frame.Padding);
            var size=ApplyAndMeasure("frameImage",file,Path.Combine(directory,"frame-out"),accepted.Image);
            return(size==(280,180)&&preview==(280,180),$"output={size} preview={preview}");
        }));
        scenes.Add(("editor collage",async(window,directory)=>
        {
            var red=SolidFixture(directory,"collage-red.png",0,0,255);var blue=SolidFixture(directory,"collage-blue.png",255,0,0);
            var result=ToolEditor.ShowDetailedAsync(window,"createCollage",[red,blue]);
            for(var n=0;n<100&&ToolEditor.Active is null;n++)await Task.Delay(50);
            var editor=ToolEditor.Active??throw new InvalidOperationException("editor did not open");await Task.Delay(600);editor.UpdateLayout();
            editor.FieldForTest("Canvas width").Text="400";editor.FieldForTest("Canvas height").Text="200";editor.FieldForTest("Spacing").Text="0";editor.FieldForTest("Padding").Text="0";
            editor.MoveInputForTest(1,-1);
            await Task.Delay(500);await editor.WaitForPreviewForTest();
            var preview=editor.PreviewSizeForTest;
            SaveRender(editor,Path.Combine(directory,"collage.png"));
            editor.PressForTest(Key.Enter);
            var accepted=await result;
            if(accepted is null)return(false,"Enter did not accept");
            var destination=Path.Combine(directory,"collage-out");Directory.CreateDirectory(destination);
            var output=ImageEngine.Load(ImageEngine.Apply("createCollage",accepted.OrderedInputs,destination,accepted.Image,"png").Single());
            // Blue was moved first, so it fills the left cell.
            var left=Pixel(output,100,100);var right=Pixel(output,300,100);
            var order=left.B>200&&left.R<50&&right.R>200&&right.B<50;
            return((output.Width,output.Height)==(400,200)&&preview==(400,200)&&order,$"output={output.Width}x{output.Height} preview={preview} left={left} right={right}");
        }));
        scenes.Add(("editor redact",async(window,directory)=>
        {
            var file=EditorFixture(directory);var (editor,result)=await OpenEditor(window,"redactImage",file);
            editor.DragRedactionForTest(new(20,20),new(80,60));editor.DragRedactionForTest(new(120,10),new(180,40));
            var two=editor.RedactionCountForTest==2;editor.RemoveRedactionForTest(1);var one=editor.RedactionCountForTest==1;
            editor.SelectRedactionStyleForTest("solid");
            await Task.Delay(300);
            SaveRender(editor,Path.Combine(directory,"redact.png"));
            editor.PressForTest(Key.Enter);
            var accepted=await result;
            if(accepted is null)return(false,"Enter did not accept");
            if(accepted.Image.Redactions.Length!=1||accepted.Image.Redactions[0]!=new RectD(20,20,60,40))return(false,"regions "+string.Join(";",accepted.Image.Redactions));
            var destination=Path.Combine(directory,"redact-out");Directory.CreateDirectory(destination);
            var output=ImageEngine.Load(ImageEngine.Apply("redactImage",[file],destination,accepted.Image,"png").Single());var source=ImageEngine.Load(file);
            var covered=Pixel(output,50,40) is {B:0,G:0,R:0};var untouched=Pixel(output,150,50)==Pixel(source,150,50)&&Pixel(output,150,25)==Pixel(source,150,25);
            return(two&&one&&covered&&untouched,$"two={two} one={one} covered={covered} untouched={untouched}");
        }));
        scenes.Add(("editor annotate",async(window,directory)=>
        {
            var file=EditorFixture(directory);var (editor,result)=await OpenEditor(window,"annotate",file);
            editor.FieldForTest("Ink color").Text="#FF0000";editor.FieldForTest("Stroke width").Text="6";
            editor.SelectDrawingToolForTest("Pen");editor.DrawAnnotationForTest(new(20,50),new(180,50),new(60,50),new(100,50),new(140,50));
            editor.SelectDrawingToolForTest("Rectangle");editor.DrawAnnotationForTest(new(10,10),new(60,30));
            editor.SelectDrawingToolForTest("Arrow");editor.DrawAnnotationForTest(new(120,90),new(180,75));
            editor.SelectDrawingToolForTest("Text");editor.FieldForTest("Font size").Text="14";editor.DrawAnnotationForTest(new(100,10),new(100,10));
            var marks=editor.AnnotationCountForTest;
            editor.PressForTest(Key.Z,KeyModifiers.Control);var undone=editor.AnnotationCountForTest==marks-1;
            editor.SelectDrawingToolForTest("Text");editor.DrawAnnotationForTest(new(100,10),new(100,10));
            await Task.Delay(300);
            SaveRender(editor,Path.Combine(directory,"annotate.png"));
            editor.PressForTest(Key.Enter);
            var accepted=await result;
            if(accepted is null)return(false,"Enter did not accept");
            var annotated=accepted.Image.Annotated;if(annotated is null)return(false,"no annotated image");
            var source=ImageEngine.Load(file);
            var onStroke=Pixel(annotated,100,50);var changed=onStroke.R>200&&onStroke.G<60&&onStroke.B<60&&onStroke!=Pixel(source,100,50);
            var clean=Near(Pixel(annotated,150,30),Pixel(source,150,30));
            var destination=Path.Combine(directory,"annotate-out");Directory.CreateDirectory(destination);
            var output=ImageEngine.Load(ImageEngine.Apply("annotate",[file],destination,accepted.Image,"png").Single());
            var saved=(output.Width,output.Height)==(200,100)&&Pixel(output,100,50)==onStroke;
            return((annotated.Width,annotated.Height)==(200,100)&&marks==4&&undone&&changed&&clean&&saved,$"size={annotated.Width}x{annotated.Height} marks={marks} undone={undone} stroke={onStroke} changed={changed} clean={clean} saved={saved}");
        }));
    }

    private static (byte B,byte G,byte R,byte A) Pixel(PixelImage image,int x,int y){var i=(y*image.Width+x)*4;return(image.Bgra[i],image.Bgra[i+1],image.Bgra[i+2],image.Bgra[i+3]);}
    private static bool Near((byte B,byte G,byte R,byte A) a,(byte B,byte G,byte R,byte A) b)=>Math.Abs(a.B-b.B)<=3&&Math.Abs(a.G-b.G)<=3&&Math.Abs(a.R-b.R)<=3;
    private static string SolidFixture(string directory,string name,byte b,byte g,byte r)
    {
        var file=Path.Combine(directory,name);var pixels=new byte[100*100*4];
        for(var i=0;i<pixels.Length;i+=4){pixels[i]=b;pixels[i+1]=g;pixels[i+2]=r;pixels[i+3]=255;}
        File.WriteAllBytes(file,ImageEngine.PngBytes(new PixelImage(100,100,pixels)));return file;
    }
}
