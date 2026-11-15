using Avalonia.Input;
using Nashi.Core;
using Nashi.Engine;
using Nashi.Linux.Platform;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
namespace Nashi.Linux;

// Documents end to end: PDF tools through the wheel flow, PDF to images, the organizer, and the Office hint when LibreOffice is missing.
internal static partial class SmokeTests
{
    /// A PDF whose pages are solid blocks of distinct colors, so reordering is visible in the organizer.
    private static string DocumentFixture(string directory,string name,params (byte R,byte G,byte B)[] colors)
    {
        var file=Path.Combine(directory,name);
        using var document=new PdfDocument();
        foreach(var (r,g,b) in colors)
        {
            var page=document.AddPage();page.Width=XUnit.FromPoint(300);page.Height=XUnit.FromPoint(400);
            using var graphics=XGraphics.FromPdfPage(page);graphics.DrawRectangle(new XSolidBrush(XColor.FromArgb(r,g,b)),30,30,240,340);
        }
        document.Save(file);return file;
    }
    private static async Task<T> Await<T>(Func<T?> probe,string what)where T:class
    {
        for(var n=0;n<200;n++){if(probe() is {} value)return value;await Task.Delay(50);}
        throw new InvalidOperationException(what+" did not appear");
    }
    static partial void DocumentScenes(List<(string Name,Scene Run)> scenes)
    {
        scenes.Add(("documents merge two PDFs through RunToolAsync",async(window,directory)=>
        {
            var work=Path.Combine(directory,"doc-merge");Directory.CreateDirectory(work);
            var a=DocumentFixture(work,"a.pdf",(200,60,60),(60,200,60));var b=DocumentFixture(work,"b.pdf",(60,60,200));
            window.ClearFilesForTest();window.AddFiles([a,b]);var recorded=window.RecordedForTest=[];
            try
            {
                var run=window.RunToolAsync(new ToolChoice("mergePDFs","Merge PDFs","▣"));
                var editor=await Await(()=>ToolEditor.Active,"editor");await Task.Delay(500);editor.PressForTest(Key.Enter);
                await run.WaitAsync(TimeSpan.FromSeconds(30));
                var output=recorded.SingleOrDefault().Paths?.SingleOrDefault();if(output is null||!File.Exists(output))return(false,"no merged file");
                using var merged=Services.Pdf!.Open(output);
                return(merged.PageCount==3,$"{Path.GetFileName(output)} pages={merged.PageCount}");
            }
            finally{window.RecordedForTest=null;window.ClearFilesForTest();}
        }));
        scenes.Add(("documents PDF to PNG",async(window,directory)=>
        {
            var work=Path.Combine(directory,"doc-png");Directory.CreateDirectory(work);
            var pdf=DocumentFixture(work,"pages.pdf",(200,60,60),(60,200,60));
            window.ClearFilesForTest();window.AddFiles([pdf]);
            try
            {
                var outputs=await window.ConvertAsync("png");
                var sizes=outputs.Select(o=>ImageEngine.Load(o)).Select(i=>i.Width+"x"+i.Height).ToArray();
                return(outputs.Count==2&&outputs.All(File.Exists),$"{outputs.Count} PNGs {string.Join(", ",sizes)}");
            }
            finally{window.ClearFilesForTest();}
        }));
        scenes.Add(("documents organizer reorder and render",async(window,directory)=>
        {
            var work=Path.Combine(directory,"doc-organize");Directory.CreateDirectory(work);
            var pdf=DocumentFixture(work,"deck.pdf",(200,60,60),(60,200,60),(60,60,200),(220,180,50));
            window.ClearFilesForTest();window.AddFiles([pdf]);var recorded=window.RecordedForTest=[];
            try
            {
                var run=window.RunToolAsync(new ToolChoice("organizePDF","Organize PDF","▤"));
                var organizer=await Await(()=>PdfOrganizer.Active,"organizer");await Task.Delay(800);
                organizer.SelectForTest(3);organizer.MoveForTest(-1);organizer.MoveForTest(-1);   // 1,4,2,3
                organizer.SelectForTest(0);organizer.RotateForTest();organizer.RemoveForTest();     // 4,2,3 with page 1 gone
                await Task.Delay(300);
                var order=string.Join(",",organizer.Pages);var detail=RenderWindow(organizer,Path.Combine(directory,"organizer.png"));
                organizer.AcceptForTest();await run.WaitAsync(TimeSpan.FromSeconds(30));
                var output=recorded.SingleOrDefault().Paths?.SingleOrDefault();if(output is null||!File.Exists(output))return(false,"no organized file; order "+order);
                using var result=Services.Pdf!.Open(output);
                return(order=="4,2,3"&&result.PageCount==3,$"order={order} pages={result.PageCount}; {detail}");
            }
            finally{window.RecordedForTest=null;window.ClearFilesForTest();}
        }));
        scenes.Add(("documents Office route shows the setup hint",async(window,directory)=>
        {
            if(Services.Office is not {} office)return(false,"no Office service");
            if(office.Executable is not null)return(true,"LibreOffice is installed; hint check skipped");
            var work=Path.Combine(directory,"doc-office");Directory.CreateDirectory(work);var note=Path.Combine(work,"note.txt");File.WriteAllText(note,"hello");
            window.ClearFilesForTest();window.AddFiles([note]);
            try
            {
                try{await window.ConvertAsync("docx");return(false,"conversion unexpectedly succeeded");}
                catch(InvalidOperationException error){return(error.Message==office.SetupHint,error.Message);}
            }
            finally{window.ClearFilesForTest();}
        }));
    }
}
