using Avalonia.Controls;
using Avalonia.Threading;
using Nashi.Engine;
using Nashi.Linux.Platform;
namespace Nashi.Linux;
// Port of the frameImage and createCollage parts of Nashi.Windows/PhotoEditorOptions.cs: option fields, debounced live preview through ImageEngine.Frame / ImageEngine.Collage.
internal sealed partial class ToolEditor
{
    private readonly ComboBox backdrop=new(){ItemsSource=new[]{"solid","gradient","image"},SelectedIndex=0},collageLayout=new(){ItemsSource=new[]{"grid","row","column","featured"},SelectedIndex=0};
    private readonly CheckBox fitCollage=new(){Content="Fill cells (crop to fit)",IsChecked=true};
    private string? backgroundImage;
    private void BuildComposeOptions()
    {
        if(tool is not ("frameImage" or "createCollage"))return;
        if(tool=="frameImage")
        {
            options.Children.Add(L10n.Label(new TextBlock{Text="Background",Margin=new(4,12,4,0)}));options.Children.Add(backdrop);TextField("Background color","#FFFFFF");TextField("Gradient end","#FFD85C");
            options.Children.Add(Ui.Button("Choose background image…",async()=>
            {
                try{var picked=await Services.Files.OpenFilesAsync(this,L10n.Text("Choose background image…"),false,["png","jpg","webp","heic","avif","tiff","bmp"]);if(picked.Count>0){backgroundImage=picked[0];backdrop.SelectedItem="image";QueuePhotoPreview();}}
                catch(Exception error)when(error is IOException or InvalidOperationException){ToolTip.SetTip(this,error.Message);}
            }));
            Field("Padding",80);Field("Corner radius",24);Field("Shadow radius",24);Field("Shadow opacity",.25);Field("Background blur",0);Field("Image scale",1);Field("Position X",.5);Field("Position Y",.5);
            // Empty canvas size means image size plus twice the padding.
            TextField("Canvas width","");TextField("Canvas height","");
            resetPhotoView=()=>{backdrop.SelectedIndex=0;backgroundImage=null;};
        }
        else
        {
            options.Children.Add(L10n.Label(new TextBlock{Text="Layout",Margin=new(4,12,4,0)}));options.Children.Add(collageLayout);
            Field("Columns",2);Field("Canvas width",2048);Field("Canvas height",2048);Field("Spacing",12);Field("Padding",16);Field("Corner radius",0);TextField("Background color","#FFFFFF");
            options.Children.Add(L10n.Content(fitCollage));fitCollage.IsCheckedChanged+=(_,_)=>QueuePhotoPreview();
            resetPhotoView=()=>{collageLayout.SelectedIndex=0;fitCollage.IsChecked=true;};
        }
        photoPreview=new(){Interval=TimeSpan.FromMilliseconds(300)};photoPreview.Tick+=async(_,_)=>{photoPreview.Stop();await PreviewCompose();};
        foreach(var box in fields.Values)box.TextChanged+=(_,_)=>QueuePhotoPreview();
        backdrop.SelectionChanged+=(_,_)=>QueuePhotoPreview();collageLayout.SelectionChanged+=(_,_)=>QueuePhotoPreview();
        Closed+=(_,_)=>photoPreview.Stop();Opened+=(_,_)=>QueuePhotoPreview();
    }
    private int? OptionalInt(string key)=>string.IsNullOrWhiteSpace(fields[key].Text)?null:(int)Value(key);
    private FrameOptions ReadFrame()=>new((int)Value("Padding",80),Value("Corner radius",24),fields["Background color"].Text??"#FFFFFF",backdrop.SelectedItem as string??"solid",fields["Gradient end"].Text??"#FFD85C",backgroundImage,Value("Background blur"),Value("Shadow radius",24),Value("Shadow opacity",.25),OptionalInt("Canvas width"),OptionalInt("Canvas height"),Value("Image scale",1),Value("Position X",.5),Value("Position Y",.5));
    private CollageOptions ReadCollage()=>new(collageLayout.SelectedItem as string??"grid",(int)Value("Columns",2),(int)Value("Canvas width",2048),(int)Value("Canvas height",2048),(int)Value("Spacing",12),(int)Value("Padding",16),Value("Corner radius"),fields["Background color"].Text??"#FFFFFF",fitCollage.IsChecked==true);
    // Frame, collage, redact and annotate options come from the editor's own controls; collage order is the reordered OrderedInputs, so Order stays null.
    private ImageOptions ComposeSettings(ImageOptions original)=>tool switch
    {
        "frameImage"=>original with{Frame=ReadFrame()},
        "createCollage"=>original with{Collage=ReadCollage()},
        "redactImage"=>original with{Redactions=ReadRedactions(),RedactionStyle=redactionStyle.SelectedItem as string??"solid"},
        "annotate"=>original with{Annotated=RenderAnnotations()},
        _=>original
    };
    // Previews a copy limited to 1200 px (frame) or a 900 px canvas (collage) and scales the pixel settings with it.
    private async Task PreviewCompose()
    {
        if(!IsVisible)return;var generation=++photoGeneration;
        if(!await previewGate.WaitAsync(0)){QueuePhotoPreview();return;}
        try
        {
            PixelImage result;
            if(tool=="frameImage")
            {
                if(source is null)return;var image=source;var frame=ReadFrame();var factor=Math.Min(1,1200d/Math.Max(image.Width,image.Height));
                result=await Task.Run(()=>
                {
                    if(factor>=1)return ImageEngine.Frame(image,frame);
                    var small=ImageEngine.TransformPhoto(image,new ImageOptions(Math.Max(1,(int)Math.Round(image.Width*factor)),Math.Max(1,(int)Math.Round(image.Height*factor))));
                    return ImageEngine.Frame(small,frame with{Padding=(int)Math.Round(frame.Padding*factor),CornerRadius=frame.CornerRadius*factor,Blur=frame.Blur*factor,Shadow=frame.Shadow*factor,Width=frame.Width is {} w?Math.Max(1,(int)Math.Round(w*factor)):null,Height=frame.Height is {} h?Math.Max(1,(int)Math.Round(h*factor)):null});
                });
            }
            else
            {
                var inputs=OrderedInputs;var collage=ReadCollage();var factor=Math.Min(1,900d/Math.Max(collage.Width,collage.Height));
                result=await Task.Run(()=>ImageEngine.Collage(inputs,factor<1?collage with{Width=Math.Max(1,(int)Math.Round(collage.Width*factor)),Height=Math.Max(1,(int)Math.Round(collage.Height*factor)),Spacing=(int)Math.Round(collage.Spacing*factor),Padding=(int)Math.Round(collage.Padding*factor),CornerRadius=collage.CornerRadius*factor}:collage,CancellationToken.None));
            }
            if(!IsVisible||generation!=photoGeneration)return;SetPhotoPreview(Bitmaps.ToBitmap(result));
        }
        catch(Exception error)when(error is ArgumentException or FormatException or OverflowException or InvalidDataException or ImageMagick.MagickException or IOException or NotSupportedException){ToolTip.SetTip(this,L10n.Text(error.Message));}
        finally{previewGate.Release();}
    }
    // Test seams for Smoke/ImageEditorChecks.cs.
    internal (int Width,int Height) PreviewSizeForTest=>((int)drawing.Width,(int)drawing.Height);
    internal void MoveInputForTest(int from,int delta)
    {
        var next=from+delta;if(from<0||from>=inputOrder.Items.Count||next<0||next>=inputOrder.Items.Count)return;
        var item=inputOrder.Items[from];inputOrder.Items.RemoveAt(from);inputOrder.Items.Insert(next,item);inputOrder.SelectedIndex=next;
        OrderedInputs=inputOrder.Items.Cast<ListBoxItem>().Select(x=>(string)x.Tag!).ToArray();QueuePhotoPreview();
    }
}
