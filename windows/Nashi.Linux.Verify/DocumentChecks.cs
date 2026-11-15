using System.IO.Compression;
using Nashi.Core;
using Nashi.Engine;
using UglyToad.PdfPig.Writer;
namespace Nashi.Linux.Verify;

internal static partial class Program
{
    // Stands in for PDFium: every page is the same solid PNG.
    private sealed class FakePdfRenderer(int pages):IPdfRenderer
    {
        public IPdfDocument Open(string path,string password="")=>throw new NotSupportedException();
        public Task<byte[][]> RenderAsync(string path,int dpi=144,CancellationToken token=default,string password="")=>Task.FromResult(Enumerable.Range(0,pages).Select(_=>Page(dpi)).ToArray());
        public Task<Dictionary<int,byte[]>> RenderPagesAsync(string path,IReadOnlyCollection<int> numbers,int dpi=144,CancellationToken token=default,string password="")=>Task.FromResult(numbers.ToDictionary(n=>n,_=>Page(dpi)));
        public void Shutdown(){}
        private static byte[] Page(int dpi)=>ImageEngine.PngBytes(new PixelImage(dpi,dpi/2,Enumerable.Repeat((byte)200,dpi*dpi/2*4).ToArray()));
    }
    private sealed class FakeRecognizer(bool available):ITextRecognizer
    {
        public bool Available=>available;public string SetupHint=>"fake hint";
        public Task<string> RecognizeAsync(byte[] png,CancellationToken token)=>Task.FromResult("scanned words");
        public Task<IReadOnlyList<RectD>> WordBoxesAsync(PixelImage image,CancellationToken token)=>Task.FromResult<IReadOnlyList<RectD>>([]);
    }
    private static async Task<string?> Throws(Func<Task> action)
    {try{await action();return null;}catch(Exception error){return error.Message;}}
    static partial void DocumentChecks()
    {
        var dir=Path.Combine(Path.GetTempPath(),"nashi-document-checks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            var office=new LinuxOfficeRuntime();
            Check(!office.CanInstall,"LinuxOfficeRuntime cannot install");
            Check(office.SetupHint.Contains("libreoffice"),"LinuxOfficeRuntime setup hint names libreoffice");
            Check(office.Executable is null||Path.IsPathFullyQualified(office.Executable),"LinuxOfficeRuntime returns an absolute path");
            var tesseract=new TesseractRecognizer();
            Check(tesseract.Available||tesseract.SetupHint.Contains("tesseract"),"TesseractRecognizer unavailable gives a hint");

            var tools=new Toolchain(Path.Combine(dir,"tools"));
            string Pdf(string name,string? text)
            {
                var path=Path.Combine(dir,name);var builder=new PdfDocumentBuilder();var page=builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
                if(text is not null)page.AddText(text,12,new UglyToad.PdfPig.Core.PdfPoint(40,700),builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica));
                File.WriteAllBytes(path,builder.Build());return path;
            }
            var textPdf=Pdf("text.pdf","Hello Nashi documents");var blankPdf=Pdf("blank.pdf",null);
            var conversion=new DocumentConversion(office,tools,new FakePdfRenderer(2),new FakeRecognizer(true));
            var progress=new Progress<OperationProgress>(_=>{});

            // PDF text extraction needs no office tools.
            var outDir=Path.Combine(dir,"txt");
            var written=conversion.ConvertAsync([textPdf],"txt",outDir,new(),progress,CancellationToken.None).GetAwaiter().GetResult();
            Check(written.Count==1&&Path.GetFileName(written[0])=="text.txt"&&File.ReadAllText(written[0]).Contains("Hello Nashi documents"),"pdf to txt extracts text");
            written=conversion.ConvertAsync([blankPdf],"txt",outDir,new(),progress,CancellationToken.None).GetAwaiter().GetResult();
            Check(File.ReadAllText(written[0]).Contains("scanned words"),"blank pdf page goes through the recognizer");
            var noOcr=new DocumentConversion(office,tools,new FakePdfRenderer(1),new FakeRecognizer(false));
            var message=Throws(()=>noOcr.ConvertAsync([blankPdf],"txt",Path.Combine(dir,"noocr"),new(),progress,CancellationToken.None)).GetAwaiter().GetResult();
            Check(message=="fake hint","scanned pdf without OCR reports the setup hint");

            // PDF to images goes through the injected renderer.
            var images=conversion.ConvertAsync([textPdf],"png",Path.Combine(dir,"png"),new(Dpi:96),progress,CancellationToken.None).GetAwaiter().GetResult();
            Check(images.Count==2&&Path.GetFileName(images[0])=="text Page 1.png"&&Path.GetFileName(images[1])=="text Page 2.png","pdf to png names one file per page");
            Check(ImageEngine.Load(images[0]).Width==96,"pdf to png writes the rendered page");
            var jpgs=conversion.ConvertAsync([textPdf],"jpg",Path.Combine(dir,"jpg"),new(Dpi:96),progress,CancellationToken.None).GetAwaiter().GetResult();
            Check(jpgs.Count==2&&Path.GetFileName(jpgs[1])=="text Page 2.jpg"&&ImageEngine.Load(jpgs[1]).Width==96,"pdf to jpg encodes each page");

            // Fixed-layout EPUB from rendered pages.
            var epub=conversion.ConvertAsync([textPdf],"epub",Path.Combine(dir,"epub"),new(Dpi:96,EpubLayout:"fixed"),progress,CancellationToken.None).GetAwaiter().GetResult();
            using(var zip=ZipFile.OpenRead(epub[0]))
            {
                Check(zip.Entries[0].FullName=="mimetype"&&zip.GetEntry("OEBPS/page1.png") is not null&&zip.GetEntry("OEBPS/content.opf") is not null,"fixed epub holds mimetype, pages and package");
                using var reader=new StreamReader(zip.GetEntry("OEBPS/page0.xhtml")!.Open());Check(reader.ReadToEnd().Contains("width=96,height=48"),"fixed epub page keeps the rendered size");
            }

            // PDF to slides builds a flat ODP source from the rendered pages.
            var slides=new PdfDocumentMedia(new FakePdfRenderer(2),new FakeRecognizer(true)).Slides(textPdf,dir,72,"",CancellationToken.None).GetAwaiter().GetResult();
            var fodp=File.ReadAllText(slides);Check(fodp.Contains("Page 2")&&fodp.Contains("office:binary-data")&&System.Xml.Linq.XDocument.Parse(fodp).Root is not null,"pdf slides source is well-formed");

            // Subtitles convert without office tools.
            var srt=Path.Combine(dir,"cue.srt");File.WriteAllText(srt,"1\n00:00:01,000 --> 00:00:02,500\nHello\n");
            var vtt=conversion.ConvertAsync([srt],"vtt",Path.Combine(dir,"vtt"),new(),progress,CancellationToken.None).GetAwaiter().GetResult();
            Check(Path.GetFileName(vtt[0])=="cue.vtt"&&File.ReadAllText(vtt[0]).StartsWith("WEBVTT"),"srt to vtt");

            // Unsupported routes keep the Windows error text.
            message=Throws(()=>conversion.ConvertAsync([textPdf],"mp3",Path.Combine(dir,"bad"),new(),progress,CancellationToken.None)).GetAwaiter().GetResult();
            Check(message=="This conversion is not available for text.pdf.","unsupported route error");

            // Office routes.
            var docs=Path.Combine(dir,"note.txt");File.WriteAllText(docs,"Plain note\n");
            if(office.Executable is null)
            {
                message=Throws(()=>conversion.ConvertAsync([docs],"docx",Path.Combine(dir,"office"),new(),progress,CancellationToken.None)).GetAwaiter().GetResult();
                Check(message is not null&&message.Contains("Set up office"),"office route without LibreOffice explains setup");
                Console.WriteLine("skipped: txt/pdf to docx, pdf to xlsx, pdf to pptx (LibreOffice not installed)");
            }
            else
            {
                var docx=conversion.ConvertAsync([docs],"docx",Path.Combine(dir,"office"),new(),progress,CancellationToken.None).GetAwaiter().GetResult();
                Check(Path.GetFileName(docx[0])=="note.docx"&&new FileInfo(docx[0]).Length>0,"txt to docx through LibreOffice");
                var pdfFromTxt=conversion.ConvertAsync([docs],"pdf",Path.Combine(dir,"office-pdf"),new(),progress,CancellationToken.None).GetAwaiter().GetResult();
                Check(Path.GetFileName(pdfFromTxt[0])=="note.pdf"&&new FileInfo(pdfFromTxt[0]).Length>0,"txt to pdf through LibreOffice");
            }

            if(!tesseract.Available)Console.WriteLine("skipped: TesseractRecognizer recognition (tesseract not installed)");
            else
            {
                using var text=new ImageMagick.MagickImage(ImageMagick.MagickColors.White,600,160);
                new ImageMagick.Drawing.Drawables().FontPointSize(64).FillColor(ImageMagick.MagickColors.Black).Text(20,100,"HELLO WORLD").Draw(text);
                var png=text.ToByteArray(ImageMagick.MagickFormat.Png);
                Check(tesseract.RecognizeAsync(png,CancellationToken.None).GetAwaiter().GetResult().Contains("HELLO"),"tesseract recognizes text");
                Check(tesseract.WordBoxesAsync(ImageEngine.FromPng(png),CancellationToken.None).GetAwaiter().GetResult().Count>=2,"tesseract returns word boxes");
            }
        }
        finally{try{Directory.Delete(dir,true);}catch(IOException){}}
    }
}
