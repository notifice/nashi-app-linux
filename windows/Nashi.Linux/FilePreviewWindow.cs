using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nashi.Core;
using Nashi.Engine;
using Nashi.Linux.Platform;
namespace Nashi.Linux;

// Port of Nashi.Windows/FilePreviewWindow.cs. One preview lifecycle; each navigation cancels the previous load.
// Office documents are deferred until the office runtime lands; they show the "use Open" message.
internal sealed class FilePreviewWindow:Window
{
    private readonly string[] paths;private int index;
    private readonly Toolchain tools;
    private readonly string temporary=Path.Combine(Path.GetTempPath(),"Nashi-quick-preview-"+Guid.NewGuid().ToString("N"));
    private CancellationTokenSource lifetime=new();
    private readonly ContentControl content=new();
    private readonly TextBlock caption=new(){TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};
    private IMediaPlayer? player;private bool playing;
    private readonly Slider scrub=new(){Minimum=0,Maximum=1,MinWidth=140};
    private IPdfDocument? pdfDocument;private IPdfRenderer? ownedRenderer;
    private readonly StackPanel transport=new(){Orientation=Orientation.Horizontal,Spacing=8,IsVisible=false};private bool updating;
    // Image zoom: 0 fits the window, otherwise a factor of the natural size.
    private double zoom;private Image? zoomImage;private ScrollViewer? zoomScroll;private PixelSize zoomNatural;
    private Func<int,Task>? pdfPage;
    internal string PreviewStatus=>content.Content is TextBlock text?text.Text??"":content.Content?.GetType().Name??"Empty";
    internal string CurrentPath=>paths[index];internal bool Playing=>playing;
    internal Task Loading{get;private set;}=Task.CompletedTask;
    internal FilePreviewWindow(IEnumerable<string> paths,Toolchain tools)
    {
        this.tools=tools;
        this.paths=paths.Distinct().ToArray();if(this.paths.Length==0)throw new ArgumentException("Choose a file to preview.");
        Title="Preview · Nashi";Width=800;Height=620;MinWidth=560;MinHeight=400;WindowStartupLocation=WindowStartupLocation.CenterOwner;Nashi.Linux.Theme.Attach(this);
        var root=new DockPanel{Margin=new(20)};Content=root;var header=new DockPanel{Margin=new(0,0,0,14)};DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var navigation=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6};DockPanel.SetDock(navigation,Dock.Right);header.Children.Add(navigation);
        navigation.Children.Add(Ui.Button("←",()=>Navigate(-1)));navigation.Children.Add(Ui.Button("→",()=>Navigate(1)));
        navigation.Children.Add(Ui.Button("Open",()=>{if(File.Exists(CurrentPath)||Directory.Exists(CurrentPath))Services.Shell.OpenUrl(new Uri(CurrentPath).AbsoluteUri);}));header.Children.Add(caption);
        var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);transport.Children.Add(Ui.Button("Play / pause",Toggle));transport.Children.Add(scrub);footer.Children.Add(transport);
        footer.Children.Add(L10n.Label(new TextBlock{Text="←/→ files · Images: Ctrl+wheel or +/− zoom, 0 fits · PDF: PageUp/PageDown · Media: Space play/pause, ←/→ 1s, Alt+←/→ files · Esc closes",FontSize=11,Foreground=Nashi.Linux.Theme.Brush("Muted"),Margin=new(0,12,0,0),TextWrapping=TextWrapping.Wrap}));
        root.Children.Add(content);
        scrub.PropertyChanged+=(_,e)=>{if(e.Property==RangeBase.ValueProperty&&!updating&&player is not null)player.Position=TimeSpan.FromSeconds(scrub.Value);};
        AddHandler(KeyDownEvent,OnKey,Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Opened+=(_,_)=>Loading=Load();
        Closed+=(_,_)=>{lifetime.Cancel();lifetime.Dispose();CloseMedia();pdfDocument?.Dispose();pdfDocument=null;_=Cleanup();};
    }
    private void OnKey(object? sender,KeyEventArgs e)
    {
        var key=e.Key;var modifiers=e.KeyModifiers;
        if(key==Key.Escape){Close();e.Handled=true;return;}
        if(modifiers==KeyModifiers.None&&key==Key.Space){if(player is null)Close();else Toggle();e.Handled=true;}
        else if(modifiers is KeyModifiers.None or KeyModifiers.Alt&&key is Key.Left or Key.Right)
        {
            var direction=key==Key.Left?-1:1;
            if(player is not null&&modifiers==KeyModifiers.None)player.Position=TimeSpan.FromSeconds(Math.Clamp(player.Position.TotalSeconds+direction,0,Math.Max(0,player.Duration.TotalSeconds)));
            else Navigate(direction);
            e.Handled=true;
        }
        else if(modifiers==KeyModifiers.None&&key is Key.PageUp or Key.PageDown&&pdfPage is not null){_=pdfPage(key==Key.PageUp?-1:1);e.Handled=true;}
        else if(modifiers==KeyModifiers.None&&zoomImage is not null&&key is Key.OemPlus or Key.Add or Key.OemMinus or Key.Subtract or Key.D0 or Key.NumPad0)
        {
            if(key is Key.D0 or Key.NumPad0)SetZoom(0);else SetZoom(Math.Clamp((zoom==0?FitZoom():zoom)*(key is Key.OemPlus or Key.Add?1.25:.8),.1,16));
            e.Handled=true;
        }
    }
    internal void Navigate(int delta){if(paths.Length<2)return;index=(index+delta+paths.Length)%paths.Length;Loading=Load();}
    private void CloseMedia()
    {
        if(player is not null){var old=player;player=null;try{old.Pause();old.Dispose();}catch(Exception){}}
        playing=false;transport.IsVisible=false;zoomImage=null;zoomScroll=null;pdfPage=null;
    }
    private void Toggle(){if(player is null)return;if(playing)player.Pause();else player.Play();playing=!playing;}
    private async Task Cleanup(){for(var n=0;n<10;n++){try{if(Directory.Exists(temporary))Directory.Delete(temporary,true);return;}catch(IOException){await Task.Delay(250);}catch(UnauthorizedAccessException){return;}}}
    private static TextBlock Message(string text)=>L10n.Label(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Margin=new(20)});

    private double FitZoom()=>zoomScroll is null||zoomNatural.Width<1?1:Math.Min(Math.Max(1,zoomScroll.Bounds.Width)/zoomNatural.Width,Math.Max(1,zoomScroll.Bounds.Height)/zoomNatural.Height);
    private void SetZoom(double value)
    {
        if(zoomImage is null)return;zoom=value;
        if(zoom==0)
        {
            zoomImage.Stretch=Stretch.Uniform;zoomImage.Width=double.NaN;zoomImage.Height=double.NaN;zoomImage.HorizontalAlignment=HorizontalAlignment.Center;zoomImage.VerticalAlignment=VerticalAlignment.Center;
            if(zoomScroll is not null){zoomImage.MaxWidth=Math.Max(1,zoomScroll.Bounds.Width);zoomImage.MaxHeight=Math.Max(1,zoomScroll.Bounds.Height);}
        }
        else{zoomImage.Stretch=Stretch.Fill;zoomImage.MaxWidth=double.PositiveInfinity;zoomImage.MaxHeight=double.PositiveInfinity;zoomImage.Width=zoomNatural.Width*zoom;zoomImage.Height=zoomNatural.Height*zoom;}
    }
    // Image inside a ScrollViewer: Ctrl+wheel zooms, left-drag pans, scroll bars appear once zoomed past the window.
    private Control ZoomView(PixelImage pixels)
    {
        var picture=new Image{Source=Bitmaps.ToBitmap(pixels)};zoomImage=picture;zoomNatural=new(pixels.Width,pixels.Height);zoom=0;
        var scroll=new ScrollViewer{HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=picture};zoomScroll=scroll;
        scroll.SizeChanged+=(_,_)=>{if(zoom==0)SetZoom(0);};SetZoom(0);
        scroll.AddHandler(PointerWheelChangedEvent,(object? _,PointerWheelEventArgs e)=>{if(e.KeyModifiers!=KeyModifiers.Control)return;SetZoom(Math.Clamp((zoom==0?FitZoom():zoom)*(e.Delta.Y>0?1.15:1/1.15),.1,16));e.Handled=true;},Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Point? last=null;
        scroll.PointerPressed+=(_,e)=>{if(e.GetCurrentPoint(scroll).Properties.IsLeftButtonPressed){last=e.GetPosition(scroll);e.Pointer.Capture(scroll);}};
        scroll.PointerMoved+=(_,e)=>{if(last is {} from){var now=e.GetPosition(scroll);scroll.Offset=new(Math.Max(0,scroll.Offset.X-(now.X-from.X)),Math.Max(0,scroll.Offset.Y-(now.Y-from.Y)));last=now;}};
        scroll.PointerReleased+=(_,e)=>{last=null;e.Pointer.Capture(null);};
        return scroll;
    }

    private async Task Load()
    {
        lifetime.Cancel();lifetime.Dispose();lifetime=new();var token=lifetime.Token;CloseMedia();pdfDocument?.Dispose();pdfDocument=null;content.Content=Message("Preparing preview…");
        var path=CurrentPath;caption.Text=$"{Path.GetFileName(path)}  ·  {index+1} / {paths.Length}";Title=Path.GetFileName(path)+" · Preview · Nashi";
        try
        {
            if(Directory.Exists(path)){content.Content=Message("Folder · "+Path.GetFileName(path)+"\nUse Open to view its contents.");return;}
            if(!File.Exists(path))throw new FileNotFoundException("This file has moved or was removed.");
            var extension=FeatureCatalog.Extension(path);
            if(new[]{"docx","doc","rtf","rtfd","pages","xlsx","xls","ods","pptx","ppt","odp","epub"}.Contains(extension))
            {content.Content=Message("Use Open to preview this document in its default app.");return;}
            if(FeatureCatalog.IsImage(path))
            {
                var pixels=await Task.Run(()=>ImageEngine.Load(path),token);if(token.IsCancellationRequested)return;
                content.Content=ZoomView(pixels);
            }
            else if(extension=="pdf")await LoadPdf(path,token);
            else if(FeatureCatalog.IsVideo(path)||FeatureCatalog.IsAudio(path))await LoadMedia(path,token);
            else if(new[]{"txt","md","csv","tsv","srt","vtt","json","html","xml","log"}.Contains(extension))
            {
                if(new FileInfo(path).Length>2_000_000)throw new InvalidDataException("This text is too large to preview. Use Open to read the complete file.");
                var text=await File.ReadAllTextAsync(path,token);
                if(!token.IsCancellationRequested)content.Content=new TextBox{Text=text,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,FontFamily=new("monospace")};
            }
            else content.Content=Message("Use Open to preview this document in its default app.");
        }
        catch(OperationCanceledException){}
        catch(Exception error){if(!token.IsCancellationRequested)content.Content=Message("Preview unavailable. "+error.Message);}
    }

    private async Task LoadPdf(string path,CancellationToken token)
    {
        // Load pages on demand rather than decoding every page before showing the first.
        var renderer=Services.Pdf??(ownedRenderer??=new PdfRenderer());
        var pdf=await Task.Run(()=>renderer.Open(path),token);if(token.IsCancellationRequested){pdf.Dispose();return;}pdfDocument=pdf;
        var pageIndex=0;var panel=new DockPanel();var controls=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,Margin=new(0,8,0,0)};DockPanel.SetDock(controls,Dock.Bottom);panel.Children.Add(controls);
        var image=new Image{Stretch=Stretch.Uniform};panel.Children.Add(image);var counter=new TextBlock{VerticalAlignment=VerticalAlignment.Center};
        async Task Page(int delta)
        {
            try
            {
                token.ThrowIfCancellationRequested();pageIndex=Math.Clamp(pageIndex+delta,0,pdf.PageCount-1);var requested=pageIndex;
                var pixels=await Task.Run(()=>pdf.RenderPage(requested,144,token),token);
                if(!token.IsCancellationRequested&&requested==pageIndex){image.Source=Bitmaps.ToBitmap(pixels);counter.Text=$"Page {pageIndex+1} of {pdf.PageCount}";}
            }
            catch(OperationCanceledException){}
            catch(Exception error){if(!token.IsCancellationRequested&&ReferenceEquals(pdfDocument,pdf))content.Content=Message("Preview unavailable. "+error.Message);}
        }
        pdfPage=Page;
        controls.Children.Add(Ui.Button("Previous page",()=>_=Page(-1)));controls.Children.Add(Ui.Button("Next page",()=>_=Page(1)));controls.Children.Add(counter);
        content.Content=panel;await Page(0);
    }

    private async Task LoadMedia(string path,CancellationToken token)
    {
        if(Services.MediaPlayer is {} factory)
        {
            var current=factory();player=current;transport.IsVisible=true;content.Content=current.View;
            current.PositionChanged+=()=>Dispatcher.UIThread.Post(()=>{if(current!=player)return;updating=true;scrub.Maximum=Math.Max(.001,current.Duration.TotalSeconds);scrub.Value=Math.Min(scrub.Maximum,current.Position.TotalSeconds);updating=false;});
            current.Ended+=()=>Dispatcher.UIThread.Post(()=>{if(current==player){current.Pause();playing=false;}});
            current.Failed+=message=>Dispatcher.UIThread.Post(()=>{if(token.IsCancellationRequested||current!=player)return;CloseMedia();content.Content=Message("Set up media tools for this codec, or use Open to preview in its default app. "+message);});
            await current.OpenAsync(path,token);return;
        }
        // No player yet: a still frame from ffmpeg, so the preview still shows something.
        var note=L10n.Label(new TextBlock{Text="Playback preview isn't available yet",Foreground=Nashi.Linux.Theme.Brush("Muted"),HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,10,0,0)});
        var panel=new DockPanel();DockPanel.SetDock(note,Dock.Bottom);panel.Children.Add(note);
        if(FeatureCatalog.IsVideo(path)&&tools.Ready)
        {
            Directory.CreateDirectory(temporary);var frame=Path.Combine(temporary,Guid.NewGuid().ToString("N")+".png");
            try
            {
                await ProcessRunner.RunAsync(tools.Executable("ffmpeg"),["-v","error","-nostdin","-y","-i",path,"-frames:v","1","-vf","scale=w='min(1280,iw)':h=-2",frame],token);
                if(File.Exists(frame)){var pixels=await Task.Run(()=>ImageEngine.Load(frame),token);panel.Children.Add(new Image{Source=Bitmaps.ToBitmap(pixels),Stretch=Stretch.Uniform});}
            }
            catch(OperationCanceledException){throw;}catch(Exception){}
        }
        if(panel.Children.Count==1)panel.Children.Add(Message(FeatureCatalog.IsAudio(path)?"Audio · "+Path.GetFileName(path):"No still frame is available. Set up media tools or use Open."));
        if(!token.IsCancellationRequested)content.Content=panel;
    }
}
