using System.Globalization;
using Nashi.Core;
namespace Nashi.Engine;

/// OCR through the tesseract command line tool when the distribution provides it.
public sealed class TesseractRecognizer:ITextRecognizer
{
    private static string? Executable=>LinuxOfficeRuntime.FindOnPath("tesseract");
    // The binary is useless without a language pack; see Pick for which installed packs are used.
    private static readonly Lazy<string[]> Languages=new(()=>
    {
        if(Executable is not {} tesseract)return [];
        try{return ProcessRunner.RunAsync(tesseract,["--list-langs"]).GetAwaiter().GetResult().Output.Split('\n').Skip(1).Select(l=>l.Trim()).Where(l=>l.Length>0&&l!="osd").ToArray() is var all?Pick(all):[];}
        catch(Exception error)when(error is IOException or InvalidOperationException){return [];}
    });
    // Loading every installed pack makes each call slow and less accurate on large installs.
    // Prefer the app's UI languages (English, Turkish); otherwise fall back to the first installed pack.
    private static string[] Pick(string[] installed)=>installed.Where(l=>l is "eng" or "tur").ToArray() is {Length:>0} preferred?preferred:installed.Take(1).ToArray();
    public bool Available=>Executable is not null&&Languages.Value.Length>0;
    public string SetupHint=>"Install Tesseract with your package manager, e.g. sudo pacman -S tesseract tesseract-data-eng / sudo apt install tesseract-ocr";
    public async Task<string> RecognizeAsync(byte[] png,CancellationToken token)
    {
        var tesseract=Available?Executable!:throw new InvalidOperationException(SetupHint);
        return (await Run(tesseract,png,false,token)).TrimEnd();
    }
    public async Task<IReadOnlyList<RectD>> WordBoxesAsync(PixelImage image,CancellationToken token)
    {
        var tesseract=Available?Executable!:throw new InvalidOperationException(SetupHint);var boxes=new List<RectD>();
        // TSV columns: level page block par line word left top width height conf text. Level 5 rows are words.
        foreach(var line in (await Run(tesseract,ImageEngine.PngBytes(image),true,token)).Split('\n'))
        {
            var cells=line.TrimEnd('\r').Split('\t');if(cells.Length<12||cells[0]!="5"||string.IsNullOrWhiteSpace(cells[11]))continue;
            if(double.TryParse(cells[6],NumberStyles.Float,CultureInfo.InvariantCulture,out var x)&&double.TryParse(cells[7],NumberStyles.Float,CultureInfo.InvariantCulture,out var y)&&double.TryParse(cells[8],NumberStyles.Float,CultureInfo.InvariantCulture,out var w)&&double.TryParse(cells[9],NumberStyles.Float,CultureInfo.InvariantCulture,out var h)&&w>0&&h>0)boxes.Add(new(x,y,w,h));
        }
        return boxes;
    }
    private static async Task<string> Run(string tesseract,byte[] png,bool tsv,CancellationToken token)
    {
        var folder=Path.Combine(Path.GetTempPath(),"nashi-ocr-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var file=Path.Combine(folder,"page.png");await File.WriteAllBytesAsync(file,png,token);
            var args=new List<string>{file,"stdout","-l",string.Join("+",Languages.Value)};if(tsv)args.Add("tsv");
            return (await ProcessRunner.RunAsync(tesseract,args,token)).Output;
        }
        finally{try{Directory.Delete(folder,true);}catch(IOException){}}
    }
}
