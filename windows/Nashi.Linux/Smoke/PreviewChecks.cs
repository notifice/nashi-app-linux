using Avalonia;
using Avalonia.Media.Imaging;
using Nashi.Engine;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
namespace Nashi.Linux;

internal static partial class SmokeTests
{
    private static async Task<(bool Ok,string? Detail)> PreviewScene(MainWindow window,string directory,string file,string? png,Func<FilePreviewWindow,bool> check)
    {
        var preview=window.PreviewFiles([file]);
        try
        {
            await Task.Delay(300);await preview.Loading;await Task.Delay(500);preview.UpdateLayout();
            if(png is not null)
            {
                var size=new PixelSize(Math.Max(1,(int)preview.Bounds.Width),Math.Max(1,(int)preview.Bounds.Height));
                using var bmp=new RenderTargetBitmap(size,new Vector(96,96));bmp.Render(preview);bmp.Save(Path.Combine(directory,png),new PngBitmapEncoderOptions());
            }
            return(check(preview),preview.PreviewStatus);
        }
        finally{preview.Close();}
    }

    static partial void PreviewScenes(List<(string Name,Scene Run)> scenes)
    {
        scenes.Add(("preview image",(window,directory)=>
        {
            var file=Path.Combine(directory,"preview-source.png");const int w=320,h=200;var bgra=new byte[w*h*4];
            for(var y=0;y<h;y++)for(var x=0;x<w;x++){var i=(y*w+x)*4;bgra[i]=(byte)(x*255/w);bgra[i+1]=(byte)(y*255/h);bgra[i+2]=(byte)(255-x*255/w);bgra[i+3]=255;}
            File.WriteAllBytes(file,ImageEngine.PngBytes(new PixelImage(w,h,bgra)));
            return PreviewScene(window,directory,file,"preview-image.png",p=>p.PreviewStatus=="ScrollViewer");
        }));
        scenes.Add(("preview pdf",(window,directory)=>
        {
            var file=Path.Combine(directory,"preview-source.pdf");
            using(var document=new PdfDocument())
            {
                for(var n=0;n<2;n++)
                {
                    var page=document.AddPage();page.Width=XUnit.FromPoint(300);page.Height=XUnit.FromPoint(400);
                    using var graphics=XGraphics.FromPdfPage(page);
                    graphics.DrawRectangle(n==0?XBrushes.SteelBlue:XBrushes.Tomato,40,40,220,120);graphics.DrawEllipse(XBrushes.Gold,90,220,120,120);
                }
                document.Save(file);
            }
            return PreviewScene(window,directory,file,"preview-pdf.png",p=>p.PreviewStatus=="DockPanel");
        }));
        scenes.Add(("preview text",(window,directory)=>
        {
            var file=Path.Combine(directory,"preview-source.txt");File.WriteAllText(file,"Hello preview\nsecond line");
            return PreviewScene(window,directory,file,null,p=>p.PreviewStatus=="TextBox");
        }));
    }
}
