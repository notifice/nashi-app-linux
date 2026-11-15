using Nashi.Engine;
namespace Nashi.Linux.Platform;

internal static partial class Services
{
    static partial void InitEngine(){Pdf=new PdfRenderer();Ocr=new TesseractRecognizer();Office=new LinuxOfficeRuntime();}
}
