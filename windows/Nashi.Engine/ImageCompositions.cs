using ImageMagick;
using ImageMagick.Drawing;
using Nashi.Core;
namespace Nashi.Engine;

// Magick.NET port of Nashi.Windows/ImageCompositions.cs. Layout geometry matches WPF; pixels are not byte-identical.
public static partial class ImageEngine
{
    private static void CheckCanvas(int width,int height){if(width<1||height<1||width>30000||height>30000||(long)width*height>100_000_000)throw new ArgumentException("Choose a canvas within 100 MP and 30,000 pixels.");}
    // WPF ColorConverter forms: #RGB, #ARGB, #RRGGBB, #AARRGGBB (alpha first), or a color name.
    internal static MagickColor ParseColor(string color)
    {
        _=PhotoCodec.Format("png");
        try
        {
            var text=color.Trim();
            if(text.StartsWith('#'))
            {
                var hex=text[1..];if(hex.Length is 3 or 4)hex=string.Concat(hex.Select(c=>new string(c,2)));
                if(hex.Length is not (6 or 8)||!hex.All(Uri.IsHexDigit))throw new FormatException();
                var a=hex.Length==8?Convert.ToByte(hex[..2],16):(byte)255;var o=hex.Length==8?2:0;
                return new MagickColor(Convert.ToByte(hex.Substring(o,2),16),Convert.ToByte(hex.Substring(o+2,2),16),Convert.ToByte(hex.Substring(o+4,2),16),a);
            }
            if(text.Length==0||!text.All(char.IsLetter))throw new FormatException();
            return new MagickColor(text);
        }
        catch(Exception error)when(error is FormatException or MagickException or ArgumentException){throw new ArgumentException("Choose a hex background color, such as #FFFFFF or #00FFFFFF.");}
    }
    private static MagickImage Canvas(int width,int height,MagickColor color)
    {
        CheckCanvas(width,height);_=PhotoCodec.Format("png");
        var canvas=new MagickImage(color,(uint)width,(uint)height){HasAlpha=true};canvas.ColorSpace=ColorSpace.sRGB;return canvas;
    }
    // Draws the source scaled to cover (fill) or fit the area, clipped to the area with rounded corners.
    private static void Fitted(MagickImage canvas,PixelImage source,RectD area,bool fill,double radius=0)
    {
        var scale=fill?Math.Max(area.Width/source.Width,area.Height/source.Height):Math.Min(area.Width/source.Width,area.Height/source.Height);
        var w=source.Width*scale;var h=source.Height*scale;var dx=area.X+(area.Width-w)/2;var dy=area.Y+(area.Height-h)/2;
        var x0=Math.Max(Math.Max(dx,area.X),0);var y0=Math.Max(Math.Max(dy,area.Y),0);
        var x1=Math.Min(Math.Min(dx+w,area.X+area.Width),canvas.Width);var y1=Math.Min(Math.Min(dy+h,area.Y+area.Height),canvas.Height);
        if(x1<=x0||y1<=y0)return;
        var px=(int)Math.Round(x0);var py=(int)Math.Round(y0);var pw=Math.Max(1,(int)Math.Round(x1)-px);var ph=Math.Max(1,(int)Math.Round(y1)-py);
        // Crop the source first so a large Scale never builds an oversized intermediate.
        var sx=Math.Clamp((int)Math.Floor((px-dx)/scale),0,source.Width-1);var sy=Math.Clamp((int)Math.Floor((py-dy)/scale),0,source.Height-1);
        var sw=Math.Clamp((int)Math.Ceiling(pw/scale)+1,1,source.Width-sx);var sh=Math.Clamp((int)Math.Ceiling(ph/scale)+1,1,source.Height-sy);
        using var piece=ToMagick(Crop(source,new PixelRect(sx,sy,sw,sh)));
        piece.Resize(new MagickGeometry((uint)pw,(uint)ph){IgnoreAspectRatio=true});piece.ResetPage();
        var r=Math.Min(radius,Math.Min(area.Width,area.Height)/2);
        if(r>0)
        {
            using var mask=new MagickImage(MagickColors.Transparent,(uint)pw,(uint)ph){HasAlpha=true};
            new Drawables().FillColor(MagickColors.White).RoundRectangle(area.X-px,area.Y-py,area.X+area.Width-px,area.Y+area.Height-py,r,r).Draw(mask);
            piece.Composite(mask,CompositeOperator.DstIn);
        }
        canvas.Composite(piece,px,py,CompositeOperator.Over);
    }
    // WPF LinearGradientBrush(start,end,45): start (0,0), end (cos,sin) in relative units, clamped.
    private static MagickImage Gradient(int width,int height,MagickColor from,MagickColor to)
    {
        CheckCanvas(width,height);var pixels=new byte[checked(width*height*4)];var k=Math.Cos(Math.PI/4);
        byte[] a=[(byte)from.B,(byte)from.G,(byte)from.R,(byte)from.A],b=[(byte)to.B,(byte)to.G,(byte)to.R,(byte)to.A];
        for(var y=0;y<height;y++)
        {
            for(var x=0;x<width;x++)
            {
                var t=Math.Clamp(k*((x+.5)/width+(y+.5)/height),0,1);var i=(y*width+x)*4;
                for(var c=0;c<4;c++)pixels[i+c]=(byte)Math.Round(a[c]+(b[c]-a[c])*t);
            }
        }
        return ToMagick(new PixelImage(width,height,pixels));
    }
    internal static PixelImage Frame(PixelImage image,FrameOptions o)
    {
        if(o.Padding<0||o.Padding>4000||!new[]{o.CornerRadius,o.Blur,o.Shadow,o.ShadowOpacity,o.Scale,o.X,o.Y}.All(double.IsFinite)||o.Scale<=0||o.Scale>10)throw new ArgumentException("Choose valid background and image dimensions.");
        var w=o.Width??image.Width+2*o.Padding;var h=o.Height??image.Height+2*o.Padding;CheckCanvas(w,h);
        using var canvas=o.Backdrop=="gradient"?Gradient(w,h,ParseColor(o.Color),ParseColor(o.GradientEnd)):Canvas(w,h,ParseColor(o.Color));
        if(o.Backdrop=="image"){if(o.BackgroundImage is null)throw new ArgumentException("Choose a background image.");var back=Load(o.BackgroundImage);if(o.Blur>0)back=FromPng(PhotoCodec.Adjust(PngBytes(back),new(){Blur=o.Blur}));Fitted(canvas,back,new(0,0,w,h),true);}
        var scale=Math.Min(Math.Max(1,w-2*o.Padding)/(double)image.Width,Math.Max(1,h-2*o.Padding)/(double)image.Height)*o.Scale;var iw=image.Width*scale;var ih=image.Height*scale;
        var rect=new RectD((w-iw)*Math.Clamp(o.X,0,1),(h-ih)*Math.Clamp(o.Y,0,1),iw,ih);
        if(o.Shadow>0)
        {
            var spread=Math.Min(100,o.Shadow);var alpha=(byte)(Math.Clamp(o.ShadowOpacity,0,1)*6);
            for(var n=8;n>0;n--)
            {
                var grow=spread*n/8;var radius=o.CornerRadius+grow;
                new Drawables().FillColor(new MagickColor(0,0,0,alpha)).RoundRectangle(rect.X-grow,rect.Y-grow,rect.X+rect.Width+grow,rect.Y+rect.Height+grow,radius,radius).Draw(canvas);
            }
        }
        Fitted(canvas,image,rect,true,o.CornerRadius);
        return FromMagick(canvas);
    }
    internal static PixelImage Collage(IReadOnlyList<string> inputs,CollageOptions o,CancellationToken token)
    {
        if(inputs.Count<2||inputs.Count>100)throw new ArgumentException("Choose 2–100 images for a collage.");var order=o.Order??Enumerable.Range(0,inputs.Count).ToArray();if(order.Length!=inputs.Count||!order.Order().SequenceEqual(Enumerable.Range(0,inputs.Count)))throw new ArgumentException("The image order must include every image once.");
        if(o.Columns<1||o.Columns>100||o.Padding<0||o.Spacing<0)throw new ArgumentException("Choose valid collage spacing and columns.");
        var columns=o.Layout=="row"?inputs.Count:o.Layout=="column"?1:Math.Min(o.Columns,inputs.Count);var rows=(int)Math.Ceiling(inputs.Count/(double)columns);var width=o.Width;var height=o.Height;
        using var canvas=Canvas(width,height,ParseColor(o.Color));
        var cw=(width-2d*o.Padding-(columns-1)*o.Spacing)/columns;var ch=(height-2d*o.Padding-(rows-1)*o.Spacing)/rows;if(cw<=0||ch<=0)throw new ArgumentException("Reduce padding or spacing to fit the images.");
        for(var n=0;n<order.Length;n++)
        {
            token.ThrowIfCancellationRequested();RectD cell;
            if(o.Layout=="featured")
            {
                var availableWidth=width-2d*o.Padding;var availableHeight=height-2d*o.Padding;var largeWidth=(availableWidth-o.Spacing)*.62;var smallHeight=(availableHeight-(order.Length-2)*o.Spacing)/(order.Length-1);
                if(largeWidth<=0||smallHeight<=0)throw new ArgumentException("Reduce spacing to fit the featured layout.");
                cell=n==0?new(o.Padding,o.Padding,largeWidth,availableHeight):new(o.Padding+largeWidth+o.Spacing,o.Padding+(n-1)*(smallHeight+o.Spacing),availableWidth-largeWidth-o.Spacing,smallHeight);
            }
            else cell=new(o.Padding+n%columns*(cw+o.Spacing),o.Padding+n/columns*(ch+o.Spacing),cw,ch);
            Fitted(canvas,Load(inputs[order[n]]),cell,o.Fill,o.CornerRadius);
        }
        return FromMagick(canvas);
    }
    internal static PixelImage Redact(PixelImage image,RectD[] regions,string style)
    {
        if(regions.Length==0)throw new ArgumentException("Draw at least one area to redact.");
        CheckCanvas(image.Width,image.Height);var pixels=(byte[])image.Bgra.Clone();
        foreach(var r in regions)
        {
            if(!(r.Width>=1&&r.Height>=1&&r.X>=0&&r.Y>=0&&r.X+r.Width<=image.Width&&r.Y+r.Height<=image.Height))throw new ArgumentException("Keep redaction areas inside the image.");
            var x0=(int)Math.Floor(r.X);var y0=(int)Math.Floor(r.Y);var x1=Math.Min(image.Width,(int)Math.Ceiling(r.X+r.Width));var y1=Math.Min(image.Height,(int)Math.Ceiling(r.Y+r.Height));var rw=x1-x0;var rh=y1-y0;
            byte[] area;
            if(style=="solid"){area=new byte[rw*rh*4];for(var n=0;n<area.Length;n+=4)area[n+3]=255;}
            else
            {
                var crop=Crop(image,new PixelRect(x0,y0,rw,rh));PixelImage effect;
                if(style=="pixelate")
                {
                    using var small=ToMagick(crop);small.Scale(new MagickGeometry((uint)Math.Max(1,rw/16),(uint)Math.Max(1,rh/16)){IgnoreAspectRatio=true});
                    small.Sample(new MagickGeometry((uint)rw,(uint)rh){IgnoreAspectRatio=true});small.ResetPage();effect=FromMagick(small);
                }
                else effect=FromPng(PhotoCodec.Adjust(PngBytes(crop),new(){Blur=24}));
                area=effect.Bgra;
            }
            for(var y=0;y<rh;y++)Buffer.BlockCopy(area,y*rw*4,pixels,((y0+y)*image.Width+x0)*4,rw*4);
        }
        return new PixelImage(image.Width,image.Height,pixels,image.Dpi);
    }
}
