using Nashi.Core;
namespace Nashi.Engine;

// Platform-neutral image type used everywhere WPF used BitmapSource. Straight (non-premultiplied) BGRA8, stride = Width*4.
public sealed record PixelImage(int Width,int Height,byte[] Bgra,double Dpi=96);
public readonly record struct PixelRect(int X,int Y,int Width,int Height);
public readonly record struct RectD(double X,double Y,double Width,double Height);

// Copied verbatim from Nashi.Windows/ImageCompositions.cs:6-7.
public sealed record FrameOptions(int Padding=80,double CornerRadius=24,string Color="#FFFFFF",string Backdrop="solid",string GradientEnd="#FFD85C",string? BackgroundImage=null,double Blur=0,double Shadow=24,double ShadowOpacity=.25,int? Width=null,int? Height=null,double Scale=1,double X=.5,double Y=.5);
public sealed record CollageOptions(string Layout="grid",int Columns=2,int Width=2048,int Height=2048,int Spacing=12,int Padding=16,double CornerRadius=0,string Color="#FFFFFF",bool Fill=true,int[]? Order=null);

// Mirrors Nashi.Windows/ImageEngine.cs:9-17 with WPF types replaced.
public sealed record ImageOptions(int? Width=null,int? Height=null,int Rotation=0,PixelRect? Crop=null,int Quality=85,bool Horizontal=true,PixelImage? Annotated=null)
{
    public PhotoAdjustments Adjustments{get;init;}=new();public FrameOptions Frame{get;init;}=new();public CollageOptions Collage{get;init;}=new();
    public string RedactionStyle{get;init;}="solid";public RectD[] Redactions{get;init;}=[];
    public long? TargetBytes{get;init;}public double? ReductionPercent{get;init;}
    public bool FlipHorizontal{get;init;}public bool FlipVertical{get;init;}
    public double? Scale{get;init;}public int? LongestEdge{get;init;}
    public string? EncoderPath{get;init;}
}

public interface IPdfDocument:IDisposable
{
    int PageCount{get;}
    (double Width,double Height) PageSizePoints(int index);
    PixelImage RenderPage(int index,int dpi,CancellationToken token);
}
public interface IPdfRenderer
{
    IPdfDocument Open(string path,string password="");
    Task<byte[][]> RenderAsync(string path,int dpi=144,CancellationToken token=default,string password="");
    Task<Dictionary<int,byte[]>> RenderPagesAsync(string path,IReadOnlyCollection<int> pages,int dpi=144,CancellationToken token=default,string password="");
    void Shutdown();
}
public interface ITextRecognizer
{
    bool Available{get;} string SetupHint{get;}
    Task<string> RecognizeAsync(byte[] png,CancellationToken token);
    Task<IReadOnlyList<RectD>> WordBoxesAsync(PixelImage image,CancellationToken token);
}
public interface IOfficeRuntime
{
    string? Executable{get;} bool CanInstall{get;} string SetupHint{get;}
    Task InstallAsync(IProgress<OperationProgress>? progress,CancellationToken token);
}
