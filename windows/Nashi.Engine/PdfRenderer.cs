namespace Nashi.Engine;

// pdfium-backed renderer. PdfRasterDocument serializes every native call behind one lock.
public sealed class PdfRenderer:IPdfRenderer
{
    private const long RenderedLimit=512_000_000;
    public IPdfDocument Open(string path,string password="")=>PdfRasterDocument.Open(path,password);
    public Task<byte[][]> RenderAsync(string path,int dpi=144,CancellationToken token=default,string password="")=>Task.Run(()=>
    {
        using var pdf=PdfRasterDocument.Open(path,password);var images=new List<byte[]>();long total=0;
        for(var i=0;i<pdf.PageCount;i++){token.ThrowIfCancellationRequested();var image=ImageEngine.PngBytes(pdf.RenderPage(i,dpi,token));total+=image.LongLength;if(total>RenderedLimit)throw new InvalidDataException("Rendered pages exceed 512 MB. Choose a lower DPI or split the PDF first.");images.Add(image);}return images.ToArray();
    },token);
    // Only the listed 1-based pages, so a fallback for a few pages does not render a whole document.
    public Task<Dictionary<int,byte[]>> RenderPagesAsync(string path,IReadOnlyCollection<int> pages,int dpi=144,CancellationToken token=default,string password="")=>Task.Run(()=>
    {
        using var pdf=PdfRasterDocument.Open(path,password);var images=new Dictionary<int,byte[]>();long total=0;
        foreach(var page in pages.Distinct()){token.ThrowIfCancellationRequested();if(page<1||page>pdf.PageCount)continue;var image=ImageEngine.PngBytes(pdf.RenderPage(page-1,dpi,token));total+=image.LongLength;if(total>RenderedLimit)throw new InvalidDataException("Rendered pages exceed 512 MB. Choose a lower DPI or split the PDF first.");images[page]=image;}return images;
    },token);
    public void Shutdown()=>PdfRasterDocument.Shutdown();
}
