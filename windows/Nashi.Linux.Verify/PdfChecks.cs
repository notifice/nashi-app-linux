using System.Text;
using ImageMagick;
using Nashi.Core;
using Nashi.Engine;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
namespace Nashi.Linux.Verify;

internal static partial class Program
{
    static partial void PdfChecks()
    {
        var dir=Path.Combine(Path.GetTempPath(),"nashi-pdf-checks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        var renderer=new PdfRenderer();
        try
        {
            // Plain two-page PDF: 200x100 pt and 100x200 pt, each with a black square.
            var plain=Path.Combine(dir,"plain.pdf");
            using(var doc=new PdfDocument())
            {
                var p1=doc.AddPage();p1.Width=XUnit.FromPoint(200);p1.Height=XUnit.FromPoint(100);
                var p2=doc.AddPage();p2.Width=XUnit.FromPoint(100);p2.Height=XUnit.FromPoint(200);
                foreach(var page in new[]{p1,p2}){using var g=XGraphics.FromPdfPage(page);g.DrawRectangle(XBrushes.Black,10,10,60,60);}
                doc.Save(plain);
            }
            using(var pdf=renderer.Open(plain))
            {
                Check(pdf.PageCount==2,"pdf page count");
                var s0=pdf.PageSizePoints(0);var s1=pdf.PageSizePoints(1);
                Check(Math.Abs(s0.Width-200)<.01&&Math.Abs(s0.Height-100)<.01,"pdf page size 0");
                Check(Math.Abs(s1.Width-100)<.01&&Math.Abs(s1.Height-200)<.01,"pdf page size 1");
                var at72=pdf.RenderPage(0,72,CancellationToken.None);var at144=pdf.RenderPage(0,144,CancellationToken.None);
                Check(at72.Width==200&&at72.Height==100,"render 72 dpi dimensions");
                Check(at144.Width==400&&at144.Height==200,"render 144 dpi dimensions");
                // Black square at (10,10)-(70,70) on a white page.
                int Px(PixelImage i,int x,int y)=>i.Bgra[(y*i.Width+x)*4];
                Check(Px(at72,40,40)<30&&Px(at72,150,80)>225&&at72.Bgra[3]==255,"render non-blank on white");
                try{pdf.RenderPage(5,72,CancellationToken.None);Check(false,"page index range");}catch(ArgumentOutOfRangeException){Check(true,"page index range");}
            }
            var png=renderer.RenderAsync(plain,72).GetAwaiter().GetResult();
            Check(png.Length==2&&ImageEngine.FromPng(png[0]).Width==200&&ImageEngine.FromPng(png[1]).Width==100,"RenderAsync pages");
            var some=renderer.RenderPagesAsync(plain,[2,9],72).GetAwaiter().GetResult();
            Check(some.Count==1&&some.ContainsKey(2),"RenderPagesAsync selects pages");
            Check(PdfRasterDocument.OpenDocuments==0,"pdf documents released");
            // Cancelled renders stop.
            using(var cancelled=new CancellationTokenSource())
            {
                cancelled.Cancel();using var pdf=renderer.Open(plain);
                try{pdf.RenderPage(0,72,cancelled.Token);Check(false,"cancel render");}catch(OperationCanceledException){Check(true,"cancel render");}
            }

            // Password protection.
            var locked=Path.Combine(dir,"locked.pdf");
            using(var doc=PdfReader.Open(plain,PdfDocumentOpenMode.Modify))
            {
                doc.SecuritySettings.UserPassword="secret";doc.SecuritySettings.OwnerPassword="owner";doc.Save(locked);
            }
            try{using var _=renderer.Open(locked);Check(false,"locked pdf needs password");}catch(InvalidDataException){Check(true,"locked pdf needs password");}
            try{using var _=renderer.Open(locked,"wrong");Check(false,"wrong password rejected");}catch(InvalidDataException){Check(true,"wrong password rejected");}
            using(var open=renderer.Open(locked,"secret")){Check(open.PageCount==2&&open.RenderPage(0,72,CancellationToken.None).Width==200,"password opens pdf");}
            var corrupt=Path.Combine(dir,"bad.pdf");File.WriteAllText(corrupt,"Not a PDF");
            try{using var _=renderer.Open(corrupt);Check(false,"invalid pdf");}catch(InvalidDataException){Check(true,"invalid pdf");}
            Check(PdfRasterDocument.OpenDocuments==0,"pdf error paths release documents");

            // QR code.
            var matrix=new ZXing.QrCode.QRCodeWriter().encode("https://nashi.example/qr",ZXing.BarcodeFormat.QR_CODE,0,0);
            const int scale=6,quiet=4;var size=(matrix.Width+quiet*2)*scale;var qr=new byte[size*size*4];Array.Fill(qr,(byte)255);
            for(var y=0;y<matrix.Height;y++)for(var x=0;x<matrix.Width;x++)if(matrix[x,y])
                for(var dy=0;dy<scale;dy++)for(var dx=0;dx<scale;dx++){var o=(((y+quiet)*scale+dy)*size+(x+quiet)*scale+dx)*4;qr[o]=qr[o+1]=qr[o+2]=0;}
            var decoded=QrDecoder.Decode(new PixelImage(size,size,qr));
            Check(decoded.Length==1&&decoded[0]=="https://nashi.example/qr","qr decodes");
            Check(QrDecoder.Decode(new PixelImage(64,64,Enumerable.Repeat((byte)255,64*64*4).ToArray())).Length==0,"blank image has no qr");

            // Compression of an image-only PDF.
            var noise=Path.Combine(dir,"noise.jpg");
            using(var image=new MagickImage(MagickColors.White,900,700))
            {
                image.AddNoise(NoiseType.Gaussian);image.Quality=100;image.Write(noise);
            }
            var heavy=Path.Combine(dir,"heavy.pdf");
            using(var doc=new PdfDocument())
            {
                var page=doc.AddPage();page.Width=XUnit.FromPoint(450);page.Height=XUnit.FromPoint(350);using var g=XGraphics.FromPdfPage(page);using var img=XImage.FromFile(noise);g.DrawImage(img,0,0,450,350);doc.Save(heavy);
            }
            var heavyHash=Sha(heavy);var heavySize=new FileInfo(heavy).Length;
            var outDir=Path.Combine(dir,"compressed");
            var compressed=PdfCompression.RunAsync([heavy],outDir,new ImageOptions(Quality:30),"",null,CancellationToken.None,renderer).GetAwaiter().GetResult();
            Check(compressed.Count==1&&File.Exists(compressed[0]),"compress writes output");
            Check(new FileInfo(compressed[0]).Length<heavySize,"compress output smaller");
            Check(Sha(heavy)==heavyHash,"compress keeps input");
            using(var result=renderer.Open(compressed[0])){Check(result.PageCount==1&&result.RenderPage(0,72,CancellationToken.None).Width==450,"compress output valid pdf");}

            // AcroForm field value appears in the rendered preview.
            const string appearance="q 1 1 1 rg 0 0 240 50 re f 0 g BT /F1 20 Tf 10 16 Td (NASHI FORM) Tj ET Q";
            var objects=new[]{"<< /Type /Catalog /Pages 2 0 R /AcroForm 6 0 R >>","<< /Type /Pages /Kids [3 0 R] /Count 1 >>","<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 150] /Annots [5 0 R] /Resources << /Font << /F1 4 0 R >> >> /Contents 7 0 R >>","<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>","<< /Type /Annot /Subtype /Widget /FT /Tx /T (Name) /V (NASHI FORM) /Rect [30 50 270 100] /F 4 /AP << /N 8 0 R >> /P 3 0 R >>","<< /Fields [5 0 R] /DA (/F1 20 Tf 0 g) /DR << /Font << /F1 4 0 R >> >> >>","<< /Length 0 >>\nstream\n\nendstream","<< /Type /XObject /Subtype /Form /BBox [0 0 240 50] /Resources << /Font << /F1 4 0 R >> >> /Length "+appearance.Length+" >>\nstream\n"+appearance+"\nendstream"};
            using var form=new MemoryStream();void Write(string text){form.Write(Encoding.ASCII.GetBytes(text));}Write("%PDF-1.4\n");var offsets=new List<long>{0};
            for(var n=0;n<objects.Length;n++){offsets.Add(form.Position);Write($"{n+1} 0 obj\n{objects[n]}\nendobj\n");}
            var xref=form.Position;Write($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");foreach(var offset in offsets.Skip(1))Write(offset.ToString("D10",System.Globalization.CultureInfo.InvariantCulture)+" 00000 n \n");Write($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            var formPath=Path.Combine(dir,"form.pdf");File.WriteAllBytes(formPath,form.ToArray());
            using(var pdf=renderer.Open(formPath))
            {
                var image=pdf.RenderPage(0,72,CancellationToken.None);Check(image.Width==300&&image.Height==150,"form preview dimensions");
                var dark=0;for(var y=50;y<100;y++)for(var x=30;x<270;x++)if(image.Bgra[(y*image.Width+x)*4]<100)dark++;
                Check(dark>100,"form preview shows field value");
            }

            // Rich fixture shipped with the repository.
            var rich=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../Reference/document-fixtures/rich.pdf"));
            Check(File.Exists(rich),"rich.pdf fixture found");
            using(var pdf=renderer.Open(rich)){Check(pdf.PageCount>=1&&pdf.RenderPage(0,72,CancellationToken.None).Width>0,"rich.pdf renders");}
            Check(PdfRasterDocument.OpenDocuments==0,"all pdf documents released");
            renderer.Shutdown();
        }
        finally{try{Directory.Delete(dir,true);}catch{}}
    }
}
