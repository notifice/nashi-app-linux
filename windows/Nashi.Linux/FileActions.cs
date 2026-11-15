using Avalonia.Controls;
using Nashi.Core;
using Nashi.Linux.Platform;
namespace Nashi.Linux;

// Port of Nashi.Windows/FileActions.cs. Share has no Linux counterpart and is left out; Terminal and the clipboard go through Services.
internal static class FileActions
{
    internal static Task Copy(Window owner,IReadOnlyList<string> paths)=>(Services.Clipboard??throw new InvalidOperationException("The clipboard is not available.")).SetFilesAsync(owner,paths);
    internal static Task CopyPaths(Window owner,IReadOnlyList<string> paths)=>(Services.Clipboard??throw new InvalidOperationException("The clipboard is not available.")).SetTextAsync(owner,string.Join(Environment.NewLine,paths));
    internal static void Terminal(string path)=>Services.Shell.OpenTerminal(Directory.Exists(path)?path:Path.GetDirectoryName(Path.GetFullPath(path))!);
    /// Moves files and folders into destination. Each item is copied into a staging folder beside the destination (so it is on the destination's device),
    /// size-checked, published with collision-safe names and only then removed from its source. Items already in destination stay untouched.
    internal static async Task<IReadOnlyList<string>> MoveAsync(IReadOnlyList<string> inputs,string destination,IProgress<OperationProgress>? progress,CancellationToken token)
    {
        using var tx=new ExportTransaction(destination);
        var dest=Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        var toMove=new List<string>();var staged=new List<string>();
        var seen=new HashSet<string>(StringComparer.Ordinal);var resolved=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var path in inputs)
        {
            var full=Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if(!File.Exists(full)&&!Directory.Exists(full))throw new FileNotFoundException("Source not found: "+full,full);
            if(Directory.Exists(full)&&(Services.Shell.SameDirectory(full,dest)||dest.StartsWith(full+Path.DirectorySeparatorChar,StringComparison.Ordinal)))throw new ArgumentException("Choose a destination outside the folder being moved.");
            var parent=Path.GetDirectoryName(full);
            if(parent is not null&&Services.Shell.SameDirectory(Path.TrimEndingDirectorySeparator(parent),dest)){resolved[full]=Path.Combine(dest,Path.GetFileName(full));continue;}
            if(!seen.Add(full))continue;
            var target=SafePaths.Unique(tx.StagingDirectory,Path.GetFileName(full));
            await CopyTree(full,target,token);
            staged.Add(target);toMove.Add(full);
            progress?.Report(new(staged.Count/(double)inputs.Count*.95,"Preparing move…"));
        }
        token.ThrowIfCancellationRequested();
        if(staged.Count>0)
        {
            var published=tx.Publish(staged,token);
            for(var i=0;i<toMove.Count;i++)resolved[toMove[i]]=published[i];
            // Originals go only after every copy was published; one that cannot be removed leaves the new copy in place and is reported.
            var retained=new List<string>();
            foreach(var original in toMove)
            {
                var deleted=false;
                for(var attempt=0;attempt<5;attempt++)
                {
                    try{if(Directory.Exists(original))Directory.Delete(original,true);else if(File.Exists(original))File.Delete(original);deleted=true;break;}
                    catch(Exception ex)when(attempt<4&&(ex is IOException or UnauthorizedAccessException)){await Task.Delay(25*(attempt+1),CancellationToken.None);}
                    catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){break;}
                }
                if(!deleted&&(File.Exists(original)||Directory.Exists(original)))retained.Add(original);
            }
            if(retained.Count>0)throw new IOException("Files were copied safely, but some originals could not be removed. New copies are in "+destination+". Check the originals before retrying.");
        }
        return inputs.Select(p=>resolved[Path.TrimEndingDirectorySeparator(Path.GetFullPath(p))]).ToArray();
    }
    private static async Task CopyTree(string source,string target,CancellationToken token,int depth=0)
    {
        token.ThrowIfCancellationRequested();
        if(depth>64||(File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Move regular files and folders without symbolic links.");
        if(Directory.Exists(source)){Directory.CreateDirectory(target);foreach(var item in Directory.EnumerateFileSystemEntries(source))await CopyTree(item,SafePaths.ArchiveEntry(target,Path.GetFileName(item)),token,depth+1);return;}
        long length;
        await using(var input=File.OpenRead(source))
        {
            length=input.Length;
            await using var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true);await input.CopyToAsync(output,token);
        }
        if(new FileInfo(target).Length!=length)throw new IOException("The copy of "+Path.GetFileName(source)+" does not match the original. Nothing was removed.");
        File.SetLastWriteTimeUtc(target,File.GetLastWriteTimeUtc(source));
    }
}
