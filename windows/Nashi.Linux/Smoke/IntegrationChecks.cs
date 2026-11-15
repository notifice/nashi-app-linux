using Avalonia.Input;
using Nashi.Engine;
namespace Nashi.Linux;

// Home integration: the wheel's tool flow (editor, ImageEngine, Library record) and the page and header seams.
internal static partial class SmokeTests
{
    static partial void IntegrationScenes(List<(string Name,Scene Run)> scenes)
    {
        scenes.Add(("integration run resize tool",async(window,directory)=>
        {
            var file=EditorFixture(directory);window.ClearFilesForTest();window.AddFiles([file]);
            var recorded=window.RecordedForTest=[];
            try
            {
                var run=window.RunToolAsync(new ToolChoice("resize","Resize","↗"));
                for(var n=0;n<100&&ToolEditor.Active is null;n++)await Task.Delay(50);
                var editor=ToolEditor.Active??throw new InvalidOperationException("editor did not open");
                await Task.Delay(600);editor.FieldForTest("Width").Text="100";editor.FieldForTest("Height").Text="50";editor.PressForTest(Key.Enter);
                await run.WaitAsync(TimeSpan.FromSeconds(30));
                if(recorded.Count!=1)return(false,"library records: "+recorded.Count);
                var output=recorded[0].Paths.SingleOrDefault();
                if(output is null||!File.Exists(output))return(false,"no output file");
                var image=ImageEngine.Load(output);
                var ok=(image.Width,image.Height)==(100,50)&&recorded[0].Label=="Resize"&&output!=file;
                File.Delete(output);
                return(ok,$"{recorded[0].Label} -> {Path.GetFileName(output)} {image.Width}x{image.Height}");
            }
            finally{window.RecordedForTest=null;window.ClearFilesForTest();}
        }));
        scenes.Add(("integration pages and header seams",async(window,directory)=>
        {
            window.ShowLibrary(true);var library=window.CurrentPage=="library"&&window.LibraryVisible;
            window.ShowPage("home");var home=window.CurrentPage=="home"&&!window.LibraryVisible;
            var settings=window.ShowSettings();await Task.Delay(300);var reachable=window.OpenSettings==settings;settings.Close();await Task.Delay(100);
            window.AddFiles([EditorFixture(directory)]);await Task.Delay(200);
            var png=Path.Combine(directory,"home-integrated.png");var detail=RenderWindow(window,png);
            window.ClearFilesForTest();
            return(library&&home&&reachable,$"library={library} home={home} settings={reachable}; {detail}");
        }));
    }
}
