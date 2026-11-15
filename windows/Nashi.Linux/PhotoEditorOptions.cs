using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nashi.Core;
using Nashi.Engine;
using Nashi.Linux.Platform;
namespace Nashi.Linux;
// Photo adjustments with live preview (editImage). frameImage, createCollage, redactImage and annotate are deferred:
// ImageEngine does not implement them on Linux yet.
internal sealed partial class ToolEditor
{
    private string[] collageInputs=[];
    private DispatcherTimer? photoPreview;private bool showingOriginal;private readonly SemaphoreSlim previewGate=new(1,1);private long photoGeneration;
    private readonly CheckBox flipHorizontal=new(){Content="Flip horizontally"},flipVertical=new(){Content="Flip vertically"};
    private void BuildPhotoOptions(IReadOnlyList<string> inputs)
    {
        collageInputs=inputs.ToArray();
        if(tool!="editImage")return;
        Field("Width",bitmap?.PixelSize.Width??1280);Field("Height",bitmap?.PixelSize.Height??720);Field("Degrees",0);options.Children.Add(L10n.Content(flipHorizontal));options.Children.Add(L10n.Content(flipVertical));flipHorizontal.IsCheckedChanged+=(_,_)=>QueuePhotoPreview();flipVertical.IsCheckedChanged+=(_,_)=>QueuePhotoPreview();
        var rotation=new WrapPanel();rotation.Children.Add(Ui.Button("Rotate Left",()=>RotatePhoto(-90)));rotation.Children.Add(Ui.Button("Rotate Right",()=>RotatePhoto(90)));options.Children.Add(rotation);
        PhotoGroup("Light",[("Exposure",-3,3),("Brightness",-1,1),("Contrast",0,2),("Highlights",-1,1),("Shadows",-1,1),("Whites",-1,1),("Blacks",-1,1),("Gamma",.2,3)]);
        PhotoGroup("Color",[("Temperature",-1,1),("Tint",-1,1),("Saturation",0,2),("Vibrance",-1,1),("Hue",-180,180)]);
        PhotoGroup("Detail",[("Clarity",0,1),("Texture",-1,1),("Sharpen",0,2),("Dehaze",0,1),("Grain",0,1),("NoiseReduction",0,1),("Vignette",0,2),("Fade",0,1),("Blur",0,30)]);
        photoPreview=new(){Interval=TimeSpan.FromMilliseconds(300)};photoPreview.Tick+=async(_,_)=>{photoPreview.Stop();await PreviewPhoto();};
        foreach(var box in fields.Values)box.TextChanged+=(_,_)=>QueuePhotoPreview();Closed+=(_,_)=>photoPreview.Stop();
        var original=L10n.Content(new CheckBox{Content="Show original",Margin=new(4,14,4,4)});
        original.IsCheckedChanged+=(_,_)=>{if(original.IsChecked==true){showingOriginal=true;photoPreview.Stop();photoGeneration++;SetPhotoPreview(bitmap!);}else{showingOriginal=false;QueuePhotoPreview();}};
        resetPhotoView=()=>original.IsChecked=false;previewStack.Children.Add(original);Opened+=(_,_)=>QueuePhotoPreview();
    }
    private void RotatePhoto(int degrees)
    {
        try{fields["Degrees"].Text=Number(Value("Degrees")+degrees);var width=fields["Width"].Text;fields["Width"].Text=fields["Height"].Text;fields["Height"].Text=width;QueuePhotoPreview();}
        catch(ArgumentException error){ToolTip.SetTip(this,error.Message);}
    }
    private void PhotoGroup(string title,(string Name,double Minimum,double Maximum)[] controls)
    {
        options.Children.Add(L10n.Label(new TextBlock{Text=title,FontSize=15,FontWeight=FontWeight.SemiBold,Margin=new(4,16,4,4)}));Nashi.Linux.Theme.Res((TextBlock)options.Children[^1],TextBlock.ForegroundProperty,"Muted");
        foreach(var setting in controls)
        {
            var property=typeof(PhotoAdjustments).GetProperty(setting.Name)!;var value=(double)property.GetValue(new PhotoAdjustments())!;Field(setting.Name,value);var field=fields[setting.Name];options.Children.Remove(field);var label=options.Children[^1];options.Children.Remove(label);
            var row=new DockPanel{Margin=new(4,8,4,0)};field.Width=72;field.Margin=new(4,0,0,0);field.Padding=new(7,3,7,3);DockPanel.SetDock(field,Dock.Right);row.Children.Add(field);row.Children.Add(L10n.Label(new TextBlock{Text=setting.Name=="NoiseReduction"?"Noise reduction":setting.Name,VerticalAlignment=VerticalAlignment.Center}));options.Children.Add(row);
            var slider=new Slider{Minimum=setting.Minimum,Maximum=setting.Maximum,Value=value,Margin=new(4,7,4,7)};ToolTip.SetTip(slider,setting.Name);var syncing=false;
            slider.ValueChanged+=(_,_)=>{if(syncing)return;syncing=true;field.Text=Number(Math.Round(slider.Value,3));syncing=false;};
            field.TextChanged+=(_,_)=>{if(syncing||!double.TryParse(field.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.CurrentCulture,out var number)||!double.IsFinite(number))return;syncing=true;slider.Value=number;syncing=false;};options.Children.Add(slider);
        }
    }
    // Fit preview with zoom buttons; the canvas keeps image pixel coordinates inside the Viewbox, so pointer positions need no conversion.
    private Control ZoomablePreview(Control visual)
    {
        var zoom=1d;var result=new StackPanel();var controls=new StackPanel{Orientation=Orientation.Horizontal};var viewbox=new Viewbox{Child=visual,Stretch=Stretch.Uniform,Width=600,Height=420};var scroll=new ScrollViewer{Content=viewbox,Height=420,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        void SetZoom(double value){zoom=Math.Clamp(value,.25,4);var height=Math.Clamp(Bounds.Height-360,180,420);scroll.Height=height;viewbox.Width=Math.Max(100,scroll.Bounds.Width-18)*zoom;viewbox.Height=height*zoom;}
        var percent=L10n.Label(new TextBlock{Text="100%",VerticalAlignment=VerticalAlignment.Center,Margin=new(8,0,8,0)});Nashi.Linux.Theme.Res(percent,TextBlock.ForegroundProperty,"Muted");
        void Apply(double value){SetZoom(value);percent.Text=$"{zoom*100:0}%";}
        resetPreviewZoom=()=>Apply(1);
        controls.Children.Add(Ui.Button("−",()=>Apply(zoom/1.25)));controls.Children.Add(Ui.Button("Fit",()=>Apply(1)));controls.Children.Add(Ui.Button("+",()=>Apply(zoom*1.25)));controls.Children.Add(percent);result.Children.Add(controls);result.Children.Add(scroll);
        Avalonia.Automation.AutomationProperties.SetAutomationId(scroll,"ImagePreviewViewport");scroll.SizeChanged+=(_,_)=>SetZoom(zoom);SizeChanged+=(_,_)=>SetZoom(zoom);return result;
    }
    private void QueuePhotoPreview(){if(photoPreview is null||showingOriginal)return;photoPreview.Stop();photoPreview.Start();}
    private ImageOptions PhotoSettings(ImageOptions original)
    {
        var changes=new Dictionary<string,double>();foreach(var property in typeof(PhotoAdjustments).GetProperties())changes[property.Name]=Value(property.Name,(double)property.GetValue(new PhotoAdjustments())!);
        var adjustments=System.Text.Json.JsonSerializer.Deserialize<PhotoAdjustments>(System.Text.Json.JsonSerializer.Serialize(changes))!;
        return original with{TargetBytes=Value("Target size (KB; 0 = auto)")>0?(long)(Value("Target size (KB; 0 = auto)")*1024):null,ReductionPercent=Value("Reduction (%; 0 = auto)")>0?Value("Reduction (%; 0 = auto)"):null,FlipHorizontal=flipHorizontal.IsChecked==true,FlipVertical=flipVertical.IsChecked==true,Adjustments=adjustments};
    }
    // Adjusts a copy limited to 1200 px, then applies the same flip, turn and size ImageEngine.Apply uses.
    private async Task PreviewPhoto()
    {
        if(source is null||!IsVisible||showingOriginal)return;var generation=++photoGeneration;
        if(!await previewGate.WaitAsync(0)){QueuePhotoPreview();return;}
        try
        {
            var image=source;var settings=PhotoSettings(new((int)Value("Width",image.Width),(int)Value("Height",image.Height),(int)Value("Degrees",0)));
            var factor=Math.Min(1,1200d/Math.Max(image.Width,image.Height));var sizeUntouched=SizeUntouched;
            var name=input;var result=await Task.Run(()=>
            {
                var small=factor<1?RunPipeline("resize",name,image,new(Math.Max(1,(int)Math.Round(image.Width*factor)),Math.Max(1,(int)Math.Round(image.Height*factor)))):image;
                return RunPipeline("editImage",name,small,sizeUntouched?settings with{Width=null,Height=null}:settings with{Width=Math.Max(1,(int)Math.Round((settings.Width??image.Width)*factor)),Height=Math.Max(1,(int)Math.Round((settings.Height??image.Height)*factor))});
            });
            if(!IsVisible||showingOriginal||generation!=photoGeneration)return;SetPhotoPreview(Bitmaps.ToBitmap(result));
        }
        catch(Exception error)when(error is ArgumentException or FormatException or InvalidDataException or ImageMagick.MagickException or IOException or NotSupportedException){ToolTip.SetTip(this,error.Message);}finally{previewGate.Release();}
    }
    // ImageEngine.TransformPhoto is internal and the Nashi.Engine InternalsVisibleTo names "Nashi.Linux" while this assembly
    // is called "Nashi", so the preview runs the public Apply on an in-memory image (Annotated replaces the file read).
    private static PixelImage RunPipeline(string pipeline,string name,PixelImage image,ImageOptions settings)
    {
        var directory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"nashi-preview-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{var output=ImageEngine.Apply(pipeline,[name],directory,settings with{Annotated=image},"png").Single();return ImageEngine.Load(output);}
        finally{try{Directory.Delete(directory,true);}catch(IOException){}}
    }
    private void SetPhotoPreview(Avalonia.Media.Imaging.Bitmap result)
    {
        drawing.Width=result.PixelSize.Width;drawing.Height=result.PixelSize.Height;
        if(drawing.Children[0] is Image image){image.Source=result;image.Width=result.PixelSize.Width;image.Height=result.PixelSize.Height;}
    }
}
