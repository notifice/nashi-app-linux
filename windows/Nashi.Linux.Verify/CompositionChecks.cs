using ImageMagick;
using Nashi.Core;
using Nashi.Engine;
using PdfSharp.Pdf.IO;
namespace Nashi.Linux.Verify;

internal static partial class Program
{
    static partial void CompositionChecks()
    {
        _=PhotoCodec.Format("png");
        var dir=Path.Combine(Path.GetTempPath(),"nashi-composition-checks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            string Solid(string name,MagickColor color,uint w,uint h){var path=Path.Combine(dir,name);using var image=new MagickImage(color,w,h);image.Write(path);return path;}
            // Noisy fixture so JPEG size responds to quality and dimensions.
            string Noise(string name,uint w,uint h){var path=Path.Combine(dir,name);using var image=new MagickImage(MagickColors.Gray,w,h);image.AddNoise(NoiseType.Random);image.Write(path);return path;}
            byte[] Pixel(PixelImage image,int x,int y)=>image.Bgra[((y*image.Width+x)*4)..((y*image.Width+x)*4+4)];
            bool Near(byte[] bgra,byte r,byte g,byte b,int tolerance=3)=>Math.Abs(bgra[2]-r)<=tolerance&&Math.Abs(bgra[1]-g)<=tolerance&&Math.Abs(bgra[0]-b)<=tolerance;
            var red=Solid("red.png",MagickColors.Red,40,30);var green=Solid("green.png",new MagickColor(0,255,0),40,30);var blue=Solid("blue.png",MagickColors.Blue,40,30);var yellow=Solid("yellow.png",MagickColors.Yellow,40,30);
            var hashes=new[]{red,green,blue,yellow}.ToDictionary(p=>p,Sha);

            // Frame: padded size, fixed size, backdrops.
            var framed=ImageEngine.Load(ImageEngine.Apply("frameImage",[red],Path.Combine(dir,"o-frame"),new ImageOptions{Frame=new FrameOptions(Padding:20,CornerRadius:0,Color:"#FFFFFF",Shadow:0)})[0]);
            Check(framed.Width==80&&framed.Height==70,"frame size = image + padding");
            Check(Near(Pixel(framed,40,35),255,0,0)&&Near(Pixel(framed,2,2),255,255,255),"frame centre image, white margin");
            var fixedFrame=ImageEngine.Load(ImageEngine.Apply("frameImage",[red],Path.Combine(dir,"o-frame2"),new ImageOptions{Frame=new FrameOptions(Padding:10,Width:200,Height:100)})[0]);
            Check(fixedFrame.Width==200&&fixedFrame.Height==100,"frame fixed width/height");
            var gradient=ImageEngine.Load(ImageEngine.Apply("frameImage",[red],Path.Combine(dir,"o-frame3"),new ImageOptions{Frame=new FrameOptions(Padding:30,Backdrop:"gradient",Color:"#000000",GradientEnd:"#FFFFFF",Shadow:0,CornerRadius:0)})[0]);
            Check(Pixel(gradient,1,1)[2]<40&&Pixel(gradient,gradient.Width-2,gradient.Height-2)[2]>215,"frame gradient runs dark to light");
            var rounded=ImageEngine.Load(ImageEngine.Apply("frameImage",[red],Path.Combine(dir,"o-frame4"),new ImageOptions{Frame=new FrameOptions(Padding:20,CornerRadius:12,Color:"#00FFFFFF",Shadow:0)})[0]);
            Check(Pixel(rounded,20,20)[3]<128&&Pixel(rounded,40,35)[3]==255,"frame rounded corner clipped, transparent backdrop");
            var badFrame=false;try{ImageEngine.Apply("frameImage",[red],Path.Combine(dir,"o-frame5"),new ImageOptions{Frame=new FrameOptions(Color:"nonsense1")});}catch(ArgumentException error){badFrame=error.Message.StartsWith("Choose a hex background color");}
            Check(badFrame,"frame rejects a bad color");

            // Collage: grid dimensions and order.
            var grid=ImageEngine.Load(ImageEngine.Apply("createCollage",[red,green,blue,yellow],Path.Combine(dir,"o-grid"),new ImageOptions{Collage=new CollageOptions(Width:400,Height:300,Spacing:0,Padding:0)})[0]);
            Check(grid.Width==400&&grid.Height==300,"collage canvas size");
            Check(Near(Pixel(grid,100,75),255,0,0)&&Near(Pixel(grid,300,75),0,255,0)&&Near(Pixel(grid,100,225),0,0,255)&&Near(Pixel(grid,300,225),255,255,0),"collage 2x2 grid order");
            var ordered=ImageEngine.Load(ImageEngine.Apply("createCollage",[red,green,blue,yellow],Path.Combine(dir,"o-order"),new ImageOptions{Collage=new CollageOptions(Width:400,Height:300,Spacing:0,Padding:0,Order:[3,2,1,0])})[0]);
            Check(Near(Pixel(ordered,100,75),255,255,0)&&Near(Pixel(ordered,300,225),255,0,0),"collage custom order");
            var row=ImageEngine.Load(ImageEngine.Apply("createCollage",[red,green,blue],Path.Combine(dir,"o-row"),new ImageOptions{Collage=new CollageOptions(Layout:"row",Width:300,Height:100,Spacing:0,Padding:0)})[0]);
            Check(Near(Pixel(row,50,50),255,0,0)&&Near(Pixel(row,150,50),0,255,0)&&Near(Pixel(row,250,50),0,0,255),"collage row layout");
            var column=ImageEngine.Load(ImageEngine.Apply("createCollage",[red,green],Path.Combine(dir,"o-column"),new ImageOptions{Collage=new CollageOptions(Layout:"column",Width:100,Height:200,Spacing:0,Padding:0)})[0]);
            Check(Near(Pixel(column,50,50),255,0,0)&&Near(Pixel(column,50,150),0,255,0),"collage column layout");
            var featured=ImageEngine.Load(ImageEngine.Apply("createCollage",[red,green,blue],Path.Combine(dir,"o-featured"),new ImageOptions{Collage=new CollageOptions(Layout:"featured",Width:400,Height:300,Spacing:0,Padding:0)})[0]);
            Check(Near(Pixel(featured,100,150),255,0,0)&&Near(Pixel(featured,350,75),0,255,0)&&Near(Pixel(featured,350,225),0,0,255),"collage featured layout");
            var spaced=ImageEngine.Load(ImageEngine.Apply("createCollage",[red,green],Path.Combine(dir,"o-spaced"),new ImageOptions{Collage=new CollageOptions(Columns:2,Width:200,Height:100,Spacing:20,Padding:10,Color:"#000000")})[0]);
            Check(Near(Pixel(spaced,2,2),0,0,0)&&Near(Pixel(spaced,100,50),0,0,0)&&Near(Pixel(spaced,50,50),255,0,0),"collage spacing and padding show background");
            var badCollage=false;try{ImageEngine.Apply("createCollage",[red],Path.Combine(dir,"o-bad"),new ImageOptions());}catch(ArgumentException error){badCollage=error.Message=="Choose 2–100 images for a collage.";}
            Check(badCollage,"collage needs two images");

            // Redact: region changes, everything else keeps its pixels.
            var photo=Path.Combine(dir,"photo.png");{using var image=new MagickImage(MagickColors.White,120,80);image.AddNoise(NoiseType.Random);image.Write(photo);}
            var source=ImageEngine.Load(photo);var region=new RectD(20,10,40,30);
            foreach(var style in new[]{"solid","pixelate","blur"})
            {
                var result=ImageEngine.Load(ImageEngine.Apply("redactImage",[photo],Path.Combine(dir,"o-redact-"+style),new ImageOptions{RedactionStyle=style,Redactions=[region]},"png")[0]);
                var changed=0;var outside=true;
                for(var y=0;y<source.Height;y++)for(var x=0;x<source.Width;x++)
                {
                    var same=Pixel(source,x,y).SequenceEqual(Pixel(result,x,y));
                    if(x>=20&&x<60&&y>=10&&y<40){if(!same)changed++;}else if(!same)outside=false;
                }
                Check(result.Width==120&&result.Height==80&&changed>1000&&outside,"redact "+style+" changes only the region");
            }
            var solid=ImageEngine.Load(ImageEngine.Apply("redactImage",[photo],Path.Combine(dir,"o-redact-solid2"),new ImageOptions{Redactions=[region]},"png")[0]);
            Check(Near(Pixel(solid,30,20),0,0,0,0),"redact solid is black");
            var badRedact=false;try{ImageEngine.Apply("redactImage",[photo],Path.Combine(dir,"o-redact-bad"),new ImageOptions{Redactions=[new RectD(100,10,40,30)]});}catch(ArgumentException error){badRedact=error.Message=="Keep redaction areas inside the image.";}
            Check(badRedact,"redact rejects regions outside the image");

            // Compress: reaches the target or ends smaller than the input.
            var noisy=Noise("noisy.jpg",600,400);var noisySize=new FileInfo(noisy).Length;var target=noisySize/4;
            var compressed=ImageEngine.Apply("compress",[noisy],Path.Combine(dir,"o-compress"),new ImageOptions(Quality:90){TargetBytes=target});
            Check(new FileInfo(compressed[0]).Length<=target&&Path.GetExtension(compressed[0])==".jpg","compress reaches TargetBytes");
            var percent=ImageEngine.Apply("compress",[noisy],Path.Combine(dir,"o-compress2"),new ImageOptions(Quality:90){ReductionPercent=50});
            Check(new FileInfo(percent[0]).Length<=noisySize/2,"compress ReductionPercent");
            var plain=ImageEngine.Apply("compress",[noisy],Path.Combine(dir,"o-compress3"),new ImageOptions(Quality:30));
            Check(new FileInfo(plain[0]).Length<noisySize,"compress without target is smaller than input");
            Check(ImageEngine.CompressionTarget(1000,null,25)==750&&ImageEngine.CompressionTarget(1000,500,null)==500&&ImageEngine.CompressionTarget(1000,null,null) is null,"CompressionTarget values");
            var badTarget=false;try{ImageEngine.CompressionTarget(1000,null,100);}catch(ArgumentException error){badTarget=error.Message=="Choose a positive target size or a reduction between 0 and 100%.";}
            Check(badTarget,"CompressionTarget rejects 100%");
            var unreachable=false;try{ImageEngine.Apply("compress",[noisy],Path.Combine(dir,"o-compress4"),new ImageOptions{TargetBytes=1});}catch(IOException error){unreachable=error.Message=="The requested image size could not be reached. Choose a larger target.";}
            Check(unreachable,"compress reports an unreachable target");

            // createPDF and pdf Save: page count and page size.
            var pdfFiles=ImageEngine.Apply("createPDF",[red,green,blue],Path.Combine(dir,"o-pdf"),new ImageOptions());
            using(var document=PdfReader.Open(pdfFiles[0],PdfDocumentOpenMode.Import))
            {
                Check(Path.GetFileName(pdfFiles[0])=="Images.pdf"&&document.PageCount==3,"createPDF page count = image count");
                Check(Math.Abs(document.Pages[0].Width.Point-30)<.01&&Math.Abs(document.Pages[0].Height.Point-22.5)<.01,"createPDF page size from pixels");
            }
            var singlePdf=Path.Combine(dir,"single.pdf");ImageEngine.Save(ImageEngine.Load(red),singlePdf,"pdf");
            using(var document=PdfReader.Open(singlePdf,PdfDocumentOpenMode.Import))Check(document.PageCount==1,"Save pdf writes one page");

            // annotate keeps the supplied pixels; the source file stays unchanged.
            var annotated=ImageEngine.Load(green);
            var annotateOut=ImageEngine.Load(ImageEngine.Apply("annotate",[red],Path.Combine(dir,"o-annotate"),new ImageOptions(Annotated:annotated),"png")[0]);
            Check(annotateOut.Width==40&&Near(Pixel(annotateOut,5,5),0,255,0)&&Path.GetFileNameWithoutExtension(ImageEngine.Apply("annotate",[red],Path.Combine(dir,"o-annotate2"),new ImageOptions(Annotated:annotated),"png")[0])=="red annotate","annotate exports options.Annotated");

            foreach(var pair in hashes)Check(Sha(pair.Key)==pair.Value,"input unchanged "+Path.GetFileName(pair.Key));
            Check(Sha(photo)==Sha(photo)&&Sha(noisy)==Sha(noisy),"fixtures readable");
        }
        finally{try{Directory.Delete(dir,true);}catch(IOException){}}
    }
}
