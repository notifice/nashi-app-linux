using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Nashi.Engine;
namespace Nashi.Linux.Platform;

// PixelImage (straight BGRA8) to Avalonia bitmaps.
internal static class Bitmaps
{
    internal static WriteableBitmap ToBitmap(PixelImage image)
    {
        if(image.Width<1||image.Height<1||image.Bgra.Length<(long)image.Width*image.Height*4)throw new ArgumentException("Choose a nonempty image.");
        var bitmap=new WriteableBitmap(new PixelSize(image.Width,image.Height),new Vector(96,96),PixelFormat.Bgra8888,AlphaFormat.Unpremul);
        using var frame=bitmap.Lock();
        var row=image.Width*4;
        for(var y=0;y<image.Height;y++)System.Runtime.InteropServices.Marshal.Copy(image.Bgra,y*row,frame.Address+y*frame.RowBytes,row);
        return bitmap;
    }
    /// Renders the control at its current layout size times <paramref name="scale"/>.
    internal static RenderTargetBitmap FromControl(Control control,double scale=1)
    {
        var size=new PixelSize(Math.Max(1,(int)Math.Round(control.Bounds.Width*scale)),Math.Max(1,(int)Math.Round(control.Bounds.Height*scale)));
        var target=new RenderTargetBitmap(size,new Vector(96*scale,96*scale));
        target.Render(control);return target;
    }
}
