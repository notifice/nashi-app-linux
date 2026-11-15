using ImageMagick;
using Nashi.Core;
namespace Nashi.Engine;

// Magick.NET port of Nashi.Windows/ImageEngine.cs. Pixels travel as PixelImage (straight BGRA8).
public static partial class ImageEngine
{
    public static readonly string[] Formats=["png","jpg","webp","heic","avif","bmp","gif","tiff","pdf"];
    private static readonly string[] Unsupported=["createCollage","createPDF","frameImage","redactImage","compress","annotate"];
    private const string TooLarge="This image is too large (100 MP limit).";
    private const string BadSize="Choose dimensions between 1 and 30,000 pixels (100 MP limit).";

    /// Decodes with EXIF orientation applied. Throws InvalidDataException over 100 MP.
    public static PixelImage Load(string file)
    {
        // PhotoCodec's static constructor installs the Magick policy and limits.
        _=PhotoCodec.Format("png");
        if(FeatureCatalog.Extension(file)!="svg")
        {
            var info=new MagickImageInfo(file);
            if((long)info.Width*info.Height>100_000_000||info.Width>30000||info.Height>30000)throw new InvalidDataException(TooLarge);
        }
        using var image=PhotoCodec.Read(file);
        if((long)image.Width*image.Height>100_000_000)throw new InvalidDataException(TooLarge);
        return FromMagick(image);
    }
    public static PixelImage FromPng(byte[] png)
    {
        _=PhotoCodec.Format("png");
        using var image=new MagickImage(png,new MagickReadSettings{Format=MagickFormat.Png});
        return FromMagick(image);
    }
    public static byte[] PngBytes(PixelImage image){using var magick=ToMagick(image);return magick.ToByteArray(MagickFormat.Png);}

    private static MagickImage ToMagick(PixelImage image)
    {
        _=PhotoCodec.Format("png");
        if(image.Width<1||image.Height<1||image.Bgra.Length<(long)image.Width*image.Height*4)throw new ArgumentException("Choose a nonempty image.");
        var magick=new MagickImage();magick.ReadPixels(image.Bgra,new PixelReadSettings((uint)image.Width,(uint)image.Height,StorageType.Char,PixelMapping.BGRA));
        magick.ColorSpace=ColorSpace.sRGB;return magick;
    }
    private static PixelImage FromMagick(MagickImage image)
    {
        image.ColorSpace=ColorSpace.sRGB;using var pixels=image.GetPixelsUnsafe();
        return new PixelImage((int)image.Width,(int)image.Height,pixels.ToByteArray(PixelMapping.BGRA)??throw new InvalidDataException("The image has no pixels."));
    }

    /// Metadata-free encode; JPEG/BMP flattened on white.
    public static void Save(PixelImage image,string output,string format,int quality=85,string? encoderPath=null,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        if(format=="pdf")throw new NotSupportedException("pdf is not available on Linux yet.");
        if(File.Exists(output))throw new IOException("The output file already exists.");
        var pixels=image.Bgra;
        // JPEG and BMP have no usable alpha; unflattened transparent areas would turn black.
        if(format is "jpg" or "bmp")
        {
            pixels=(byte[])image.Bgra.Clone();
            for(var n=0;n<pixels.Length;n+=4){var alpha=pixels[n+3];if(alpha==255)continue;for(var c=0;c<3;c++)pixels[n+c]=(byte)((pixels[n+c]*alpha+255*(255-alpha)+127)/255);pixels[n+3]=255;}
        }
        PhotoCodec.EncodePixels(pixels,image.Width,image.Height,output,format,quality,encoderPath,token);
    }

    public static IReadOnlyList<string> Apply(string tool,IReadOnlyList<string> inputs,string destination,ImageOptions options,string? format=null,CancellationToken token=default,IProgress<OperationProgress>? progress=null)
    {
        if(Unsupported.Contains(tool))throw new NotSupportedException($"{tool} is not available on Linux yet.");
        if(tool is not("resize" or "crop" or "rotate" or "flip" or "editImage" or "convert" or "removeMetadata"))throw new ArgumentException("Unsupported image tool.");
        using var tx=new ExportTransaction(destination);var outputs=new List<string>();var completed=0;
        foreach(var input in inputs)
        {
            token.ThrowIfCancellationRequested();var image=options.Annotated??Load(input);var ext=(format??Path.GetExtension(input).TrimStart('.')).ToLowerInvariant();if(ext=="jpeg")ext="jpg";if(ext=="tif")ext="tiff";
            if(!Formats.Contains(ext))ext="png";
            switch(tool)
            {
                case "resize":
                    var width=options.Width??(int)Math.Round(image.Width*(double)(options.Height??image.Height)/image.Height);
                    var height=options.Height??(int)Math.Round(image.Height*(double)width/image.Width);
                    if(width<1||height<1||width>30000||height>30000||(long)width*height>100_000_000)throw new ArgumentException(BadSize);
                    image=Scale(image,width,height);break;
                case "crop":if(options.Crop is not {} crop||crop.Width<1||crop.Height<1||crop.X<0||crop.Y<0||crop.X+crop.Width>image.Width||crop.Y+crop.Height>image.Height)throw new ArgumentException("Drag a crop inside the image.");image=Crop(image,crop);break;
                case "rotate":image=Rotate(image,options.Rotation);break;
                case "flip":image=Flip(image,options.Horizontal,!options.Horizontal);break;
                case "editImage":
                    image=FromPng(PhotoCodec.Adjust(PngBytes(image),options.Adjustments));
                    image=TransformPhoto(image,options);break;
                case "convert":case "removeMetadata":break;
            }
            var output=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+(tool=="convert"?"":" "+tool)+"."+ext);
            Save(image,output,ext,options.Quality,options.EncoderPath,token);outputs.Add(output);progress?.Report(new(++completed/(double)inputs.Count*.98,"Saving images…"));
        }
        return tx.Publish(outputs,token);
    }
    // Flip, turn, then size, as on macOS: Width/Height describe the finished image,
    // so Rotate Left/Right swapping the fields keeps the photo's proportions.
    internal static PixelImage TransformPhoto(PixelImage image,ImageOptions options)
    {
        if(options.FlipHorizontal||options.FlipVertical)image=Flip(image,options.FlipHorizontal,options.FlipVertical);
        image=Rotate(image,options.Rotation);var (ew,eh)=TargetSize(image,options);
        if(ew!=image.Width||eh!=image.Height){if(ew<1||eh<1||ew>30000||eh>30000||(long)ew*eh>100_000_000)throw new ArgumentException("Choose valid image dimensions.");image=Scale(image,ew,eh);}
        return image;
    }
    // One named side keeps the aspect ratio; presets scale every image by its own size.
    private static (int Width,int Height) TargetSize(PixelImage image,ImageOptions options)
    {
        int w=image.Width,h=image.Height;
        var factor=options.Scale??(options.LongestEdge is {} edge?(double?)Math.Min(1,(double)edge/Math.Max(w,h)):null);
        if(factor is {} f)return(Math.Max(1,(int)Math.Round(w*f)),Math.Max(1,(int)Math.Round(h*f)));
        return(options.Width,options.Height) switch{({} width,{} height)=>(width,height),({} width,null)=>(width,Math.Max(1,(int)Math.Round(h*(double)width/w))),(null,{} height)=>(Math.Max(1,(int)Math.Round(w*(double)height/h)),height),_=>(w,h)};
    }
    private static PixelImage Scale(PixelImage image,int width,int height)
    {
        using var magick=ToMagick(image);magick.Resize(new MagickGeometry((uint)width,(uint)height){IgnoreAspectRatio=true});magick.ResetPage();return FromMagick(magick);
    }
    private static PixelImage Crop(PixelImage image,PixelRect crop)
    {
        var result=new byte[checked(crop.Width*crop.Height*4)];
        for(var y=0;y<crop.Height;y++)Buffer.BlockCopy(image.Bgra,((crop.Y+y)*image.Width+crop.X)*4,result,y*crop.Width*4,crop.Width*4);
        return new PixelImage(crop.Width,crop.Height,result,image.Dpi);
    }
    private static PixelImage Flip(PixelImage image,bool horizontal,bool vertical)
    {
        using var magick=ToMagick(image);if(horizontal)magick.Flop();if(vertical)magick.Flip();return FromMagick(magick);
    }
    // Quarter turns are exact. Other angles are drawn clockwise onto a transparent canvas
    // large enough for the turned corners.
    internal static PixelImage Rotate(PixelImage image,int degrees)
    {
        var angle=((degrees%360)+360)%360;if(angle==0)return image;
        if(angle%90!=0)
        {
            var radians=angle*Math.PI/180;var cos=Math.Abs(Math.Cos(radians));var sin=Math.Abs(Math.Sin(radians));
            var width=(int)Math.Ceiling(image.Width*cos+image.Height*sin);var height=(int)Math.Ceiling(image.Width*sin+image.Height*cos);
            if(width>30000||height>30000||(long)width*height>100_000_000)throw new ArgumentException("Choose valid image dimensions.");
        }
        using var magick=ToMagick(image);magick.BackgroundColor=MagickColors.Transparent;magick.HasAlpha=true;magick.Rotate(angle);magick.ResetPage();return FromMagick(magick);
    }
}
