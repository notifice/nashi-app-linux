using Avalonia.Controls;
using Nashi.Linux.Platform;
namespace Nashi.Linux;

// Port of Nashi.Windows/MainWindow.FileActions.cs: copy, copy path, terminal, float and move. Share is not offered on Linux.
internal sealed partial class MainWindow
{
    private readonly List<FloatShelf> shelves=[];
    internal IReadOnlyList<FloatShelf> Shelves=>shelves;
    partial void InitFileActions()=>Closed+=(_,_)=>{foreach(var shelf in shelves.ToArray())shelf.Close();};
    /// Runs a file action id on the given files (the selection, or a Float shelf's files).
    internal async Task PerformFileAction(string action,IReadOnlyList<string> paths)
    {
        if(busy||paths.Count==0)return;
        try
        {
            switch(action)
            {
                case "copy":await FileActions.Copy(DialogOwner,paths);L10n.SetText(status,"Files copied. Paste them wherever you need.");break;
                case "copyPath":await FileActions.CopyPaths(DialogOwner,paths);L10n.SetText(status,"File paths copied.");break;
                case "terminal":FileActions.Terminal(paths[0]);break;
                case "float":
                    if(preferences.Value.FloatBehavior=="addToExisting"&&shelves.LastOrDefault(s=>s.IsVisible) is {} existing){existing.Add(paths);existing.Activate();break;}
                    var shelf=new FloatShelf(paths,(p,a)=>PerformFileAction(a,p),p=>{files.Clear();Add(p);ShowLibrary(false);Activate();});
                    shelves.Add(shelf);shelf.Closed+=(_,_)=>shelves.Remove(shelf);shelf.Show();break;
                case "move":
                    var folder=await Services.Files.OpenFolderAsync(DialogOwner,L10n.Text("Choose where to move the files"),Path.GetDirectoryName(Path.GetFullPath(paths[0])));
                    if(folder is null)break;
                    await RunWithToastAsync("Move",(report,token)=>FileActions.MoveAsync(paths,folder,report,token));
                    foreach(var gone in files.Where(p=>!File.Exists(p)&&!Directory.Exists(p)).ToArray())files.Remove(gone);
                    Refresh();foreach(var shelf2 in shelves)shelf2.Prune();break;
            }
        }
        catch(Exception error)when(error is not OperationCanceledException){await ShowInfoAsync(error.Message,"File action could not finish");}
    }
}
