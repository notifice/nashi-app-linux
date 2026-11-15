using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Nashi.Core;
using Nashi.Engine;
using Nashi.Linux.Platform;
namespace Nashi.Linux;

/// Everything an accepted editor produces. Image tools read Image and Format; media and document tasks fill the rest through their hooks.
internal sealed record ToolEditorResult(ImageOptions Image,string Format,bool PreserveMetadata,string[] OrderedInputs,MediaOptions Media,DocumentOptions Document,int[] Pages,SplitChoice Split);

// Port of Nashi.Windows/ToolEditor.cs for the image tools (crop, resize, rotate, editImage, frameImage, createCollage, redactImage, annotate, compress, convert).
// Media and document editors add their controls and options through the four partial hooks below, in their own files.
internal sealed partial class ToolEditor:Window
{
    internal MediaOptions MediaOptions{get;private set;}=new();internal ImageOptions ImageOptions{get;private set;}=new();internal string Format{get;private set;}="png";internal int[] Pages{get;set;}=[];
    internal DocumentOptions DocumentOptions{get;private set;}=new();
    // Media and document tasks implement these. Build* add controls to `options` while the window is built;
    // Read* run when settings are accepted and may replace the options (or throw ArgumentException to reject).
    partial void BuildMediaOptions();
    partial void BuildDocumentOptions();
    partial void ReadMediaOptions(ref MediaOptions mediaOptions);
    partial void ReadDocumentOptions(ref DocumentOptions documentOptions);

    /// The editor currently open through ShowAsync; the smoke tests drive it.
    internal static ToolEditor? Active{get;private set;}
    private static readonly Dictionary<string,string> Titles=new(){["crop"]="Crop",["resize"]="Resize",["rotate"]="Rotate",["editImage"]="Edit Photo",["compress"]="Compress",["convert"]="Convert",["frameImage"]="Frame",["createCollage"]="Collage",["redactImage"]="Redact Photo",["annotate"]="Annotate"};

    private readonly string tool;private readonly PixelImage? source;private readonly Bitmap? bitmap;private readonly Dictionary<string,TextBox> fields=[];private readonly Dictionary<string,string> defaults=[];
    private readonly ComboBox format=new();
    private readonly Canvas drawing=new(){Background=Brushes.Transparent,Focusable=true};private Rectangle? selection,activeRectangle;private Point origin;private bool dragging;
    private readonly StackPanel previewStack=new();
    private readonly string input;private readonly MediaInfo? media;private readonly StackPanel options=new(){Margin=new(18,0,0,0)};
    private Action? resetCompression,resetPhotoView,resetPreviewZoom,applyExportProfile;
    private bool throwOnError;

    internal ToolEditor(string tool,string title,string input,PixelImage? preview=null,IReadOnlyList<string>? inputs=null,string[]? outputFormats=null,MediaInfo? media=null)
    {
        Nashi.Linux.Theme.Attach(this);
        this.tool=tool;this.input=input;this.media=media;source=preview;bitmap=preview is null?null:Bitmaps.ToBitmap(preview);
        Title=title+" · Nashi";Width=1060;Height=780;MinWidth=800;MinHeight=560;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var grid=new Grid{Margin=new(28)};grid.RowDefinitions.Add(new(GridLength.Auto));grid.RowDefinitions.Add(new(1,GridUnitType.Star));grid.RowDefinitions.Add(new(GridLength.Auto));Content=grid;
        var heading=new StackPanel();heading.Children.Add(L10n.Label(new TextBlock{Text=title,FontSize=30,FontWeight=FontWeight.Bold}));
        var subtitle=new TextBlock{Margin=new(0,5,0,22)};Nashi.Linux.Theme.Res(subtitle,TextBlock.ForegroundProperty,"Muted");subtitle.Text=System.IO.Path.GetFileName(input)+" · "+L10n.Text("Original stays safe");heading.Children.Add(subtitle);grid.Children.Add(heading);
        var body=new Grid();body.ColumnDefinitions.Add(new(1.6,GridUnitType.Star));body.ColumnDefinitions.Add(new(1,GridUnitType.Star));Grid.SetRow(body,1);grid.Children.Add(body);
        previewStack.Children.Add(Heading("PREVIEW"));options.Children.Add(Heading("MAKE IT YOURS"));
        body.Children.Add(new ScrollViewer{Content=previewStack,VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled});
        var optionScroll=new ScrollViewer{Content=options,VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled};Grid.SetColumn(optionScroll,1);body.Children.Add(optionScroll);
        if(bitmap is not null)
        {
            drawing.Width=bitmap.PixelSize.Width;drawing.Height=bitmap.PixelSize.Height;
            drawing.Children.Add(new Image{Source=bitmap,Width=bitmap.PixelSize.Width,Height=bitmap.PixelSize.Height});
            previewStack.Children.Add(ZoomablePreview(drawing));
            drawing.PointerPressed+=BeginDraw;drawing.PointerMoved+=MoveDraw;drawing.PointerReleased+=EndDraw;
        }
        else if(media is null)previewStack.Children.Add(L10n.Label(new TextBlock{Text="Your files will be saved as new copies.\nChoose options, then save.",FontSize=21,TextWrapping=TextWrapping.Wrap,Margin=new(0,50,20,0)}));
        if(tool=="convert")
        {
            var formats=outputFormats?.ToHashSet()??(media is not null?MediaEngine.VideoFormats.Concat(MediaEngine.AudioFormats).ToHashSet():ImageEngine.Formats.ToHashSet());
            options.Children.Add(L10n.Label(new TextBlock{Text="Output format",FontWeight=FontWeight.SemiBold}));format.ItemsSource=formats.Order().ToArray();format.SelectedIndex=0;options.Children.Add(format);
        }
        if(tool=="resize"){Field("Width",bitmap?.PixelSize.Width??1280);Field("Height",bitmap?.PixelSize.Height??720);}
        if(tool=="compress")Field("Quality (%)",80);
        if(tool=="rotate")Field("Degrees",90);
        if(tool is "crop" or "cropVideo")
        {
            options.Children.Add(L10n.Label(new TextBlock{Text="Drag to select a crop. Hold Shift for a square; existing selections keep their ratio. Press Enter to save.",TextWrapping=TextWrapping.Wrap,Margin=new(4,4,4,16)}));
            Field("X",0);Field("Y",0);Field("Width",bitmap?.PixelSize.Width??media?.Video?.Width??1280);Field("Height",bitmap?.PixelSize.Height??media?.Video?.Height??720);
        }
        BuildPhotoOptions(inputs??[input]);BuildComposeOptions();BuildInputOrder(inputs??[input]);BuildRedactionOptions();BuildAnnotationOptions();BuildCropOptions();BuildCompressionOptions();BuildDocumentOptions();BuildMediaOptions();
        if(options.Children.Count==1)options.Children.Add(L10n.Label(new TextBlock{Text="Ready to make a fresh copy. No extra settings needed.",TextWrapping=TextWrapping.Wrap,Margin=new(4,20,4,0)}));
        var footer=new DockPanel{Margin=new(0,20,0,0),LastChildFill=false};Grid.SetRow(footer,2);grid.Children.Add(footer);
        var save=Ui.Primary("Save new copy  ↗",Save);DockPanel.SetDock(save,Dock.Right);footer.Children.Add(save);
        var cancel=Ui.Button("Cancel",()=>Close(false));DockPanel.SetDock(cancel,Dock.Left);footer.Children.Add(cancel);
        var reset=Ui.Button("↶ Reset",Reset);DockPanel.SetDock(reset,Dock.Left);footer.Children.Add(reset);
        var safe=L10n.Label(new TextBlock{Text="Original stays safe",FontSize=12,VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,16,0)});Nashi.Linux.Theme.Res(safe,TextBlock.ForegroundProperty,"Muted");DockPanel.SetDock(safe,Dock.Right);footer.Children.Add(safe);
        AddHandler(KeyDownEvent,OnKey,RoutingStrategies.Tunnel);
    }
    private static TextBlock Heading(string text){var block=L10n.Label(new TextBlock{Text=text,FontSize=11,FontWeight=FontWeight.SemiBold,Margin=new(4,0,0,16)});Nashi.Linux.Theme.Res(block,TextBlock.ForegroundProperty,"Muted");return block;}

    /// Opens the editor modally for the first file and returns the image options, or null when cancelled.
    internal static async Task<ImageOptions?> ShowAsync(Window owner,string tool,IReadOnlyList<string> files,string? title=null)=>(await ShowDetailedAsync(owner,tool,files,title:title))?.Image;
    /// Same as ShowAsync with everything the editor produced. outputFormats limits the convert format list; format preselects one.
    internal static async Task<ToolEditorResult?> ShowDetailedAsync(Window owner,string tool,IReadOnlyList<string> files,PreferencesStore? preferences=null,string? format=null,string[]? outputFormats=null,string? title=null,MediaInfo? media=null,PixelImage? frame=null,Toolchain? tools=null)
    {
        if(files.Count==0)throw new ArgumentException("Choose a file first.");
        // Media tools get their probe result and, for crop and redact, the first frame as the canvas picture.
        PixelImage? preview=media is null?null:frame;
        if(media is null&&FeatureCatalog.IsImage(files[0]))
        {
            try{preview=await Task.Run(()=>ImageEngine.Load(files[0]));}
            catch(Exception error)when(error is ArgumentException or FormatException or InvalidDataException or ImageMagick.MagickException or IOException or NotSupportedException or UnauthorizedAccessException){preview=null;}
        }
        var editor=new ToolEditor(tool,L10n.Text(title??Titles.GetValueOrDefault(tool,tool)),files[0],preview,files,outputFormats,media);
        if(tools is not null)editor.ConfigureMediaRuntime(tools);
        if(preferences is not null)editor.ConfigureProfiles(preferences,format);
        else if(format is not null&&editor.format.Items.Contains(format))editor.format.SelectedItem=format;
        Active=editor;
        try{return await editor.ShowDialog<bool>(owner)?new ToolEditorResult(editor.ImageOptions,editor.Format,editor.PreserveImageMetadata,editor.OrderedInputs,editor.MediaOptions,editor.DocumentOptions,editor.Pages,new SplitChoice(editor.PagesPerFile,editor.PageRanges)):null;}
        finally{if(Active==editor)Active=null;}
    }

    // Test seams for Smoke/EditorChecks.cs.
    internal TextBox FieldForTest(string name)=>fields[name];
    internal bool AcceptForTest(){throwOnError=true;return ReadSettings(false);}
    internal void SetCropForTest(int x,int y,int width,int height)=>SetCrop(new(x,y,width,height));
    internal void RotatePhotoForTest(int degrees)=>RotatePhoto(degrees);
    internal void SelectAspectForTest(string ratio)=>aspectRatio.SelectedItem=ratio;
    internal void PressForTest(Key key,KeyModifiers modifiers=KeyModifiers.None)=>RaiseEvent(new KeyEventArgs{RoutedEvent=KeyDownEvent,Key=key,KeyModifiers=modifiers,Source=this});
    /// Simulates a pointer drag on the preview in image pixels.
    internal void DragForTest(Point from,Point to,bool shift=false)
    {
        origin=from;dragging=true;shiftHeld=shift;if(BeginCropGesture(from)){MoveCropGesture(to);}else StartSelection(from);if(cropGesture is null)ExtendSelection(to);dragging=false;cropGesture=null;activeRectangle=null;
    }
    internal async Task WaitForPreviewForTest()
    {
        for(var n=0;n<100&&(photoPreview?.IsEnabled==true||previewGate.CurrentCount==0);n++)await Task.Delay(50);
    }

    private static string Number(double n)=>n.ToString("0.###",CultureInfo.CurrentCulture);
    private bool Untouched(string key)=>fields.TryGetValue(key,out var box)&&box.Text==defaults[key];
    private bool SizeUntouched=>Untouched("Width")&&Untouched("Height");
    private void Field(string name,double value)=>TextField(name,Number(value));
    private void TextField(string name,string value)
    {
        options.Children.Add(L10n.Label(new TextBlock{Text=name,Margin=new(4,12,4,0)}));var box=new TextBox{Text=value};Avalonia.Automation.AutomationProperties.SetAutomationId(box,name);
        fields.Add(name,box);defaults.Add(name,value);options.Children.Add(box);
    }
    private void Reset()
    {
        resetMedia?.Invoke();resetCompression?.Invoke();resetPhotoView?.Invoke();resetPreviewZoom?.Invoke();
        foreach(var pair in defaults)fields[pair.Key].Text=pair.Value;
        foreach(var choice in new[]{sampleDepth,documentLayout,epubLayout,aspectRatio})choice.SelectedIndex=0;
        flipHorizontal.IsChecked=flipVertical.IsChecked=false;preserveMetadata.IsChecked=true;
        if(inputOrder.Items.Count>0){inputOrder.Items.Clear();foreach(var path in collageInputs)inputOrder.Items.Add(new ListBoxItem{Content=System.IO.Path.GetFileName(path),Tag=path});OrderedInputs=collageInputs.ToArray();}
        while(drawing.Children.Count>1)drawing.Children.RemoveAt(drawing.Children.Count-1);
        dragging=false;cropGesture=null;selection=activeRectangle=null;
        applyExportProfile?.Invoke();QueuePhotoPreview();
    }
    private double Value(string key,double fallback=0)
    {
        if(!fields.TryGetValue(key,out var box))return fallback;
        if(!double.TryParse(box.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out var value)&&!double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||!double.IsFinite(value))throw new ArgumentException("Enter a valid number for "+key+".");return value;
    }

    private void BeginDraw(object? sender,PointerPressedEventArgs e)
    {
        if(tool is not ("crop" or "cropVideo" or "redactVideo")||!e.GetCurrentPoint(drawing).Properties.IsLeftButtonPressed)return;
        drawing.Focus();origin=Clamp(e.GetPosition(drawing));shiftHeld=e.KeyModifiers.HasFlag(KeyModifiers.Shift);dragging=true;e.Pointer.Capture(drawing);
        if(!BeginCropGesture(origin))StartSelection(origin);
        e.Handled=true;
    }
    private void MoveDraw(object? sender,PointerEventArgs e)
    {
        if(!dragging)return;shiftHeld=e.KeyModifiers.HasFlag(KeyModifiers.Shift);var p=Clamp(e.GetPosition(drawing));
        if(MoveCropGesture(p))return;
        ExtendSelection(p);
    }
    private void EndDraw(object? sender,PointerReleasedEventArgs e)
    {
        dragging=false;cropGesture=null;activeRectangle=null;e.Pointer.Capture(null);e.Handled=true;
    }
    private Point Clamp(Point p)=>new(Math.Clamp(p.X,0,drawing.Width),Math.Clamp(p.Y,0,drawing.Height));
    private void StartSelection(Point at)
    {
        if(selection is not null)drawing.Children.Remove(selection);
        activeRectangle=new Rectangle{Stroke=Brushes.Gold,StrokeThickness=Math.Max(2,drawing.Width/350),Fill=new SolidColorBrush(Color.FromArgb(35,224,189,82)),IsHitTestVisible=false};
        drawing.Children.Add(activeRectangle);Canvas.SetLeft(activeRectangle,at.X);Canvas.SetTop(activeRectangle,at.Y);selection=activeRectangle;
    }
    // Shift locks a square; a chosen aspect preset locks its ratio.
    private void ExtendSelection(Point p)
    {
        if(activeRectangle is null)return;var dx=p.X-origin.X;var dy=p.Y-origin.Y;
        if(shiftHeld||CropRatio() is not null)
        {
            var ratio=CropRatio()??1d;var width=Math.Min(Math.Abs(dx),Math.Abs(dy)*ratio);var height=width/ratio;p=new(origin.X+Math.CopySign(width,dx),origin.Y+Math.CopySign(height,dy));
        }
        var x=Math.Min(origin.X,p.X);var y=Math.Min(origin.Y,p.Y);var w=Math.Abs(p.X-origin.X);var h=Math.Abs(p.Y-origin.Y);
        Canvas.SetLeft(activeRectangle,x);Canvas.SetTop(activeRectangle,y);activeRectangle.Width=w;activeRectangle.Height=h;
        fields["X"].Text=Number(Math.Floor(x));fields["Y"].Text=Number(Math.Floor(y));fields["Width"].Text=Number(Math.Max(1,Math.Floor(w)));fields["Height"].Text=Number(Math.Max(1,Math.Floor(h)));
    }

    private void OnKey(object? sender,KeyEventArgs e)
    {
        var focused=TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if(focused is ComboBox {IsDropDownOpen:true})return;
        if(e.Key==Key.Escape){Close(false);e.Handled=true;return;}
        if(e.Key==Key.Enter&&e.KeyModifiers==KeyModifiers.None)
        {
            if(focused is Button)return;
            Save();e.Handled=true;return;
        }
        if(tool=="annotate"&&e.Key==Key.Z&&e.KeyModifiers==KeyModifiers.Control&&focused is not TextBox){UndoAnnotation();e.Handled=true;return;}
        if(HandleMediaKey(e,focused))e.Handled=true;
    }
    private void Save()=>ReadSettings(true);
    private bool ReadSettings(bool accept)
    {
        try
        {
            if(tool=="compress"&&(Value("Target size (KB; 0 = auto)")<0||Value("Reduction (%; 0 = auto)")<0||Value("Reduction (%; 0 = auto)")>=100))throw new ArgumentException("Choose a positive size or a reduction below 100%. Zero uses automatic compression.");
            var x=Value("X");var y=Value("Y");var w=Value("Width");var h=Value("Height");
            if(tool is "crop" or "cropVideo"&&(w<1||h<1))throw new ArgumentException("Drag an area first.");
            // The size fields start from the first image. Untouched fields and presets are left to each
            // image; in a batch an untouched side follows each image's proportions. A single image gets
            // exactly the size typed.
            var preset=tool=="compress"?CompressionPreset():null;var sized=tool is "resize" or "compress" or "editImage";var batch=collageInputs.Length>1;
            int? Side(string key,double value)=>!fields.ContainsKey(key)||sized&&(preset is not null||SizeUntouched||batch&&Untouched(key))?null:(int)value;
            var next=PhotoSettings(new(Side("Width",w),Side("Height",h),(int)Value("Degrees",tool=="rotate"?90:0),tool=="crop"?new Nashi.Engine.PixelRect((int)x,(int)y,(int)w,(int)h):null,(int)Value("Quality (%)",85),true,null))with{Scale=preset?.Scale,LongestEdge=preset?.LongestEdge};
            var mediaOptions=new MediaOptions();ReadMediaOptions(ref mediaOptions);
            var documentOptions=new DocumentOptions(144,documentLayout.SelectedItem as string??"editable",epubLayout.SelectedItem as string??"reflowable");ReadDocumentOptions(ref documentOptions);
            ImageOptions=ComposeSettings(next);MediaOptions=mediaOptions;DocumentOptions=documentOptions;Format=format.SelectedItem as string??"png";
            if(accept)Close(true);return true;
        }
        catch(Exception error)when(error is ArgumentException or FormatException or OverflowException)
        {
            if(throwOnError)throw;
            _=Services.Dialogs.InfoAsync(this,L10n.Text(error.Message),L10n.Text("Check your settings"));return false;
        }
    }
}
