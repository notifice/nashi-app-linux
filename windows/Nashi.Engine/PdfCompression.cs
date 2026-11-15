using Nashi.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
namespace Nashi.Engine;
public static class PdfCompression
{
    public static async Task<IReadOnlyList<string>> RunAsync(IReadOnlyList<string> inputs,string destination,ImageOptions options,string password,IProgress<OperationProgress>? progress,CancellationToken token,IPdfRenderer pdfRenderer)
    {
        using var tx=new ExportTransaction(destination);var outputs=new List<string>();foreach(var input in inputs)
        {
            var original=new FileInfo(input).Length;var target=CompressionTarget(original,options.TargetBytes,options.ReductionPercent);using var source=PdfReader.Open(input,password,PdfDocumentOpenMode.Import);
            var featureful=source.Internals.Catalog.Elements.ContainsKey("/AcroForm")||source.Internals.Catalog.Elements.ContainsKey("/Names")||source.Internals.Catalog.Elements.ContainsKey("/Outlines")||source.Internals.Catalog.Elements.ContainsKey("/AF")||source.Internals.Catalog.Elements.ContainsKey("/Collection")||source.Pages.Cast<PdfPage>().Any(p=>p.Elements.ContainsKey("/Annots"));var text=OfficeDocuments.PdfText(input,password);
            // Rendering a protected PDF into a new document would drop its password; DocumentEngine keeps it as it is.
            if(featureful||DocumentEngine.Protected(source)||!string.IsNullOrWhiteSpace(text))
            {
                var file=featureful?SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+" Compressed.pdf"):DocumentEngine.Pdf("compress",[input],tx.StagingDirectory,token:token,password:password)[0];if(featureful)File.Copy(input,file);
                // A rewrite can be larger than the source; keep the source before checking the target it may already meet.
                if(new FileInfo(file).Length>original)File.Copy(input,file,true);if(target is {} limit&&new FileInfo(file).Length>limit)throw new IOException("That PDF size cannot be reached while keeping selectable text, links and annotations. Choose a larger target.");outputs.Add(file);continue;
            }
            var output=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+" Compressed.pdf");var dpi=144;var quality=options.Quality;var success=false;
            for(var attempt=0;attempt<14;attempt++)
            {
                token.ThrowIfCancellationRequested();if(File.Exists(output))File.Delete(output);var images=await pdfRenderer.RenderAsync(input,dpi,token,password);using var pdf=new PdfDocument();for(var n=0;n<images.Length;n++)
                {
                    token.ThrowIfCancellationRequested();var bitmap=ImageEngine.FromPng(images[n]);var jpegPath=Path.Combine(tx.StagingDirectory,"page-"+Guid.NewGuid().ToString("N")+".jpg");
                    try
                    {
                        ImageEngine.Save(bitmap,jpegPath,"jpg",Math.Clamp(quality,1,100),token:token);using var jpeg=new MemoryStream(File.ReadAllBytes(jpegPath));using var image=XImage.FromStream(jpeg);var page=pdf.AddPage();var originalPage=source.Pages[n];var rotated=originalPage.Rotate%180!=0;page.Width=rotated?originalPage.Height:originalPage.Width;page.Height=rotated?originalPage.Width:originalPage.Height;using var drawing=XGraphics.FromPdfPage(page);drawing.DrawImage(image,0,0,page.Width.Point,page.Height.Point);
                    }
                    finally{if(File.Exists(jpegPath))File.Delete(jpegPath);}
                    progress?.Report(new(null,$"Compressing PDF · attempt {attempt+1} · page {n+1}/{images.Length}…"));
                }
                pdf.Save(output);if(target is null||new FileInfo(output).Length<=target){success=true;break;}dpi=Math.Max(36,(int)(dpi*.8));quality=Math.Max(5,(int)(quality*.75));
            }
            if(!success&&!(target is {} fits&&original<=fits))throw new IOException("The requested PDF size could not be reached. Choose a larger target.");if(!success||new FileInfo(output).Length>original)File.Copy(input,output,true);outputs.Add(output);
        }
        return tx.Publish(outputs,token);
    }
    private static long? CompressionTarget(long original,long? bytes,double? percent)
    {
        if(bytes is <=0||percent is {} n&&(!double.IsFinite(n)||n<=0||n>=100))throw new ArgumentException("Choose a positive target size or a reduction between 0 and 100%.");var target=bytes??(percent is {} p?(long)(original*(1-p/100)):null);if(target is <=0)throw new ArgumentException("Choose a larger target size.");return target;
    }
}
