using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Nashi.Linux.Platform;
namespace Nashi.Linux;

// File actions: the seven move scenarios of Nashi.Windows ParitySmokeTests.RunMoveChecks on temporary folders, clipboard actions and the Float shelf.
// A real drag-out into another application needs a second app and a pointer, so only the data side of it is checked here.
internal static partial class SmokeTests
{
    private static async Task<(bool Ok,string? Detail)> MoveScene(Func<string,Task<string?>> body)
    {
        var root=Directory.CreateTempSubdirectory("nashi-move-").FullName;
        try{var problem=await body(root);return(problem is null,problem??"ok");}
        finally{try{Directory.Delete(root,true);}catch(IOException){}}
    }
    private static string Put(string folder,string name,string text){Directory.CreateDirectory(folder);var path=Path.Combine(folder,name);File.WriteAllText(path,text);return path;}
    private static Task<IReadOnlyList<string>> Move(IReadOnlyList<string> inputs,string destination)=>FileActions.MoveAsync(inputs,destination,null,CancellationToken.None);

    static partial void FileActionScenes(List<(string Name,Scene Run)> scenes)
    {
        scenes.Add(("move to folder",(_,_)=>MoveScene(async root=>
        {
            var source=Put(root,"Move original.txt","Move fixture");var moved=await Move([source],Path.Combine(root,"Moved"));
            return !File.Exists(source)&&File.ReadAllText(moved[0])=="Move fixture"&&moved[0]==Path.Combine(root,"Moved","Move original.txt")?null:"file move failed";
        })));
        scenes.Add(("move within same folder",(_,_)=>MoveScene(async root=>
        {
            var source=Put(root,"Same folder.txt","Same folder fixture");var moved=await Move([source],root);
            return File.ReadAllText(source)=="Same folder fixture"&&!File.Exists(Path.Combine(root,"Same folder 2.txt"))&&moved.SequenceEqual([source])?null:"same-folder move deleted or renamed the original";
        })));
        scenes.Add(("move name collision",(_,_)=>MoveScene(async root=>
        {
            var folder=Path.Combine(root,"ConflictMoved");var target=Put(folder,"Conflict.txt","EXISTING");var source=Put(root,"Conflict.txt","NEW");var moved=await Move([source],folder);
            if(File.ReadAllText(target)!="EXISTING")return "the existing target was overwritten";
            var renamed=Path.Combine(folder,"Conflict 2.txt");
            return File.Exists(renamed)&&File.ReadAllText(renamed)=="NEW"&&!File.Exists(source)&&moved.SequenceEqual([renamed])?null:"the incoming file was not stored under a new name";
        })));
        scenes.Add(("move duplicate inputs",(_,_)=>MoveScene(async root=>
        {
            var folder=Path.Combine(root,"DupMoved");Directory.CreateDirectory(folder);var source=Put(root,"Duplicate.txt","DUP");var moved=await Move([source,source],folder);
            var expected=Path.Combine(folder,"Duplicate.txt");
            return Directory.GetFiles(folder).Length==1&&!File.Exists(source)&&moved.Count==2&&moved.All(p=>p==expected)?null:"duplicate inputs created extra files or wrong results";
        })));
        scenes.Add(("move mixed batch",(_,_)=>MoveScene(async root=>
        {
            var folder=Path.Combine(root,"MixedDest");var already=Put(folder,"AlreadyThere.txt","ALREADY");var incoming=Put(root,"ToMove.txt","TOMOVE");var moved=await Move([already,incoming],folder);
            if(File.ReadAllText(already)!="ALREADY"||File.Exists(Path.Combine(folder,"AlreadyThere 2.txt")))return "the file already in the destination was altered or duplicated";
            var landed=Path.Combine(folder,"ToMove.txt");
            return File.Exists(landed)&&File.ReadAllText(landed)=="TOMOVE"&&!File.Exists(incoming)&&moved.SequenceEqual([already,landed])?null:"the incoming file did not land correctly";
        })));
        scenes.Add(("move missing source",(_,_)=>MoveScene(async root=>
        {
            var missing=Path.Combine(root,"MissingSource.txt");
            try{await Move([missing],root);}catch(FileNotFoundException){return null;}
            return "a missing same-directory source did not throw FileNotFoundException";
        })));
        scenes.Add(("move via symlinked folder",(_,_)=>MoveScene(async root=>
        {
            // The Linux counterpart of the Windows junction case: the destination is the same folder reached through a symbolic link.
            var real=Path.Combine(root,"Real");var file=Put(real,"LinkDoc.txt","Link content");var link=Path.Combine(root,"Link");Directory.CreateSymbolicLink(link,real);
            var stamp=File.GetLastWriteTimeUtc(file);
            var first=await Move([Path.Combine(link,"LinkDoc.txt")],real);
            if(File.ReadAllText(file)!="Link content"||File.Exists(Path.Combine(real,"LinkDoc 2.txt"))||File.GetLastWriteTimeUtc(file)!=stamp||!first.SequenceEqual([file]))return "moving through the symlink touched or renamed the file";
            var back=await Move([file],link);
            if(!File.Exists(file)||File.Exists(Path.Combine(real,"LinkDoc 2.txt"))||!back.SequenceEqual([Path.Combine(link,"LinkDoc.txt")]))return "reverse move through the symlink touched or renamed the file";
            // A folder cannot move into itself, and a symlink inside a moved folder is refused instead of being followed.
            var folder=Path.Combine(root,"Tree");Put(folder,"a.txt","a");var inner=Path.Combine(folder,"Inner");Directory.CreateDirectory(inner);
            try{await Move([folder],inner);return "moving a folder into itself was allowed";}catch(ArgumentException){}
            File.CreateSymbolicLink(Path.Combine(folder,"bad"),"a.txt");
            try{await Move([folder],Path.Combine(root,"Elsewhere"));return "a symlink inside a moved folder was followed";}catch(InvalidDataException){}
            return Directory.Exists(folder)&&File.Exists(Path.Combine(folder,"a.txt"))?null:"a refused folder move removed the original";
        })));
        scenes.Add(("move folder tree",(_,_)=>MoveScene(async root=>
        {
            var folder=Path.Combine(root,"Album");Put(folder,"one.txt","1");Put(Path.Combine(folder,"deep"),"two.txt","22");var moved=await Move([folder],Path.Combine(root,"Archive"));
            return !Directory.Exists(folder)&&File.ReadAllText(Path.Combine(moved[0],"deep","two.txt"))=="22"&&File.ReadAllText(Path.Combine(moved[0],"one.txt"))=="1"?null:"folder tree did not move intact";
        })));
        scenes.Add(("copy and copy path actions",async(window,directory)=>
        {
            if(Services.Clipboard is null)return(false,"Services.Clipboard is null");
            var a=Put(directory,"action-a.txt","a");var b=Put(directory,"action b.txt","b");
            await window.PerformFileAction("copy",[a,b]);var files=(await Services.Clipboard.ReadAsync(window)).Files;
            if(!files.OrderBy(f=>f).SequenceEqual(new[]{a,b}.OrderBy(f=>f)))return(false,"copy: "+string.Join(", ",files));
            await window.PerformFileAction("copyPath",[a,b]);var text=(await Services.Clipboard.ReadAsync(window)).Text;
            return(text==a+Environment.NewLine+b,"copyPath: "+text);
        }));
        scenes.Add(("drag source",async(window,directory)=>
        {
            if(Services.Drag is null)return(false,"Services.Drag is null");
            // Without the original pointer press there is nothing to start a drag from, so it must decline instead of throwing.
            var started=await Services.Drag.StartFilesAsync(window,new PointerEventArgs(InputElement.PointerMovedEvent,window,new Pointer(0,PointerType.Mouse,true),window,new Point(),0,new PointerPointProperties(),KeyModifiers.None),[Put(directory,"drag.txt","d")]);
            return(!started,"declined without a press: "+!started);
        }));
        scenes.Add(("float shelf",async(window,directory)=>
        {
            var image=EditorFixture(directory);var note=Put(directory,"Shelf note.txt","note");var folder=Directory.CreateDirectory(Path.Combine(directory,"Shelf folder")).FullName;
            var before=window.Shelves.Count;await window.PerformFileAction("float",[image,note,folder]);
            if(window.Shelves.Count!=before+1)return(false,"no shelf opened");
            var shelf=window.Shelves[^1];
            try
            {
                await Task.Delay(400);shelf.SetExpanded(true);await Task.Delay(800);shelf.UpdateLayout();
                var size=new PixelSize(Math.Max(1,(int)shelf.Bounds.Width),Math.Max(1,(int)shelf.Bounds.Height));
                using(var bmp=new RenderTargetBitmap(size,new Vector(96,96))){bmp.Render(shelf);bmp.Save(Path.Combine(directory,"float.png"),new PngBitmapEncoderOptions());}
                shelf.Add([image,Path.Combine(directory,"gone.txt")]);
                var ok=shelf.Paths.Count==3&&shelf.IsExpanded&&shelf.Bounds.Width>150&&shelf.Bounds.Height>200;
                File.Delete(note);shelf.Prune();
                return(ok&&shelf.Paths.Count==2,$"{size.Width}x{size.Height}, {shelf.Paths.Count} files after prune");
            }
            finally{shelf.Close();}
        }));
    }
}
