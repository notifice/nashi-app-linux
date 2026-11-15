using Avalonia.Controls;
using Avalonia.Platform.Storage;
namespace Nashi.Linux.Platform.Linux;

internal sealed class FilePicker:IFilePicker
{
    public async Task<IReadOnlyList<string>> OpenFilesAsync(Window owner,string title,bool multiple=true,IReadOnlyList<string>? extensions=null)
    {
        var top=TopLevel.GetTopLevel(owner)??owner;
        var options=new FilePickerOpenOptions{Title=title,AllowMultiple=multiple};
        if(extensions is{Count:>0})
        {
            // Linux globs are case-sensitive, so list both cases.
            var patterns=extensions.Select(e=>e.TrimStart('*','.')).Where(e=>e.Length>0).SelectMany(e=>new[]{"*."+e.ToLowerInvariant(),"*."+e.ToUpperInvariant()}).Distinct().ToList();
            options.FileTypeFilter=[new FilePickerFileType("Supported files"){Patterns=patterns},FilePickerFileTypes.All];
        }
        var picked=await top.StorageProvider.OpenFilePickerAsync(options);
        return picked.Select(f=>f.TryGetLocalPath()).Where(p=>!string.IsNullOrEmpty(p)).Select(p=>p!).ToList();
    }
    public async Task<string?> OpenFolderAsync(Window owner,string title,string? start=null)
    {
        var top=TopLevel.GetTopLevel(owner)??owner;
        var options=new FolderPickerOpenOptions{Title=title,AllowMultiple=false};
        if(!string.IsNullOrEmpty(start)&&Directory.Exists(start))options.SuggestedStartLocation=await top.StorageProvider.TryGetFolderFromPathAsync(start);
        var picked=await top.StorageProvider.OpenFolderPickerAsync(options);
        return picked.Count>0?picked[0].TryGetLocalPath():null;
    }
}
