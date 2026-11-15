using Nashi.Core;
namespace Nashi.Engine;

// Contract stub. P1-A replaces bodies with a Magick.NET port of Nashi.Windows/ImageEngine.cs; signatures are fixed.
public static partial class ImageEngine
{
    public static readonly string[] Formats=["png","jpg","webp","heic","avif","bmp","gif","tiff","pdf"];
    /// Decodes with EXIF orientation applied. Throws InvalidDataException over 100 MP.
    public static PixelImage Load(string file)=>throw new NotImplementedException();
    public static PixelImage FromPng(byte[] png)=>throw new NotImplementedException();
    public static byte[] PngBytes(PixelImage image)=>throw new NotImplementedException();
    /// Metadata-free encode; JPEG/BMP flattened on white.
    public static void Save(PixelImage image,string output,string format,int quality=85,string? encoderPath=null,CancellationToken token=default)=>throw new NotImplementedException();
    /// Same tool ids, output naming and collision rules as Nashi.Windows ImageEngine.Apply. Returns written paths.
    public static IReadOnlyList<string> Apply(string tool,IReadOnlyList<string> inputs,string destination,ImageOptions options,string? format=null,CancellationToken token=default,IProgress<OperationProgress>? progress=null)=>throw new NotImplementedException();
}
