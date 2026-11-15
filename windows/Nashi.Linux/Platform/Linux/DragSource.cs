using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
namespace Nashi.Linux.Platform.Linux;

// Avalonia 12 starts a drag from the PointerPressed event that began the gesture, so callers keep that event and pass it as the trigger.
// Each file travels as a storage item plus one uri-list item, which is what Linux file managers and most apps accept on drop.
internal sealed class DragSource:IDragSource
{
    private static readonly DataFormat<string> UriList=DataFormat.CreateStringPlatformFormat("text/uri-list");

    public async Task<bool> StartFilesAsync(Window owner,PointerEventArgs trigger,IReadOnlyList<string> paths)
    {
        if(trigger is not PointerPressedEventArgs pressed)return false;
        var full=paths.Select(Path.GetFullPath).Where(p=>File.Exists(p)||Directory.Exists(p)).ToArray();
        if(full.Length==0)return false;
        var storage=TopLevel.GetTopLevel(owner)?.StorageProvider;
        if(storage is null)return false;
        var data=new DataTransfer();
        data.Add(DataTransferItem.Create(UriList,string.Concat(full.Select(p=>new Uri(p).AbsoluteUri+"\r\n"))));
        foreach(var path in full)
        {
            IStorageItem? item=Directory.Exists(path)?await storage.TryGetFolderFromPathAsync(path):await storage.TryGetFileFromPathAsync(path);
            if(item is not null)data.Add(DataTransferItem.CreateFile(item));
        }
        // Avalonia disposes the transfer when the drag ends.
        var effect=await DragDrop.DoDragDropAsync(pressed,data,DragDropEffects.Copy|DragDropEffects.Move|DragDropEffects.Link);
        return effect!=DragDropEffects.None;
    }
}
