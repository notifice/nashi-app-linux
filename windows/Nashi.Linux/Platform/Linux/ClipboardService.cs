using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
namespace Nashi.Linux.Platform.Linux;

// Avalonia 12 clipboard: DataTransfer items carry several formats, so one item holds the uri-list pair every Linux file manager understands.
internal sealed class ClipboardService:IClipboardService
{
    private static readonly DataFormat<string> Html=DataFormat.CreateStringPlatformFormat("text/html");
    private static readonly DataFormat<string> UriList=DataFormat.CreateStringPlatformFormat("text/uri-list");
    private static readonly DataFormat<string> GnomeFiles=DataFormat.CreateStringPlatformFormat("x-special/gnome-copied-files");

    private static IClipboard Board(Window owner)=>TopLevel.GetTopLevel(owner)?.Clipboard??throw new InvalidOperationException("The clipboard is not available.");

    public async Task<TransferContent> ReadAsync(Window owner)
    {
        var board=Board(owner);
        var files=new List<string>();
        foreach(var item in await board.TryGetFilesAsync()??[])
            if(item.TryGetLocalPath() is{Length:>0}p)files.Add(p);
        string? url=null;
        var list=await board.TryGetValueAsync(UriList);
        if(list is not null)
            foreach(var line in Lines(list))
            {
                if(!Uri.TryCreate(line,UriKind.Absolute,out var uri))continue;
                if(uri.IsFile){if(!files.Contains(uri.LocalPath))files.Add(uri.LocalPath);}
                else url??=line;
            }
        if(files.Count==0&&await board.TryGetValueAsync(GnomeFiles) is{} gnome)
            foreach(var line in Lines(gnome).Skip(1))
                if(Uri.TryCreate(line,UriKind.Absolute,out var uri)&&uri.IsFile)files.Add(uri.LocalPath);
        var text=await board.TryGetTextAsync();
        var html=await board.TryGetValueAsync(Html);
        if(url is null&&text is not null&&Uri.TryCreate(text.Trim(),UriKind.Absolute,out var u)&&(u.Scheme==Uri.UriSchemeHttp||u.Scheme==Uri.UriSchemeHttps))url=u.AbsoluteUri;
        byte[]? png=null;
        if(files.Count==0)
        {
            using var bmp=await board.TryGetBitmapAsync();
            if(bmp is not null){using var ms=new MemoryStream();bmp.Save(ms,new PngBitmapEncoderOptions());png=ms.ToArray();}
        }
        return new TransferContent(files,png,text,html,url);
    }

    private static IEnumerable<string> Lines(string value)=>value.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries).Select(s=>s.Trim()).Where(s=>s.Length>0&&!s.StartsWith('#'));

    public async Task SetTextAsync(Window owner,string text)
    {
        var board=Board(owner);
        await board.SetTextAsync(text);
        await board.FlushAsync();
    }

    public async Task SetFilesAsync(Window owner,IReadOnlyList<string> paths)
    {
        var board=Board(owner);
        var full=paths.Select(Path.GetFullPath).Where(p=>File.Exists(p)||Directory.Exists(p)).ToArray();
        if(full.Length==0)throw new InvalidOperationException("Those files are no longer available.");
        var uris=full.Select(p=>new Uri(p).AbsoluteUri).ToArray();
        var data=new DataTransfer();
        var listing=DataTransferItem.Create(UriList,string.Concat(uris.Select(u=>u+"\r\n")));
        listing.Set(GnomeFiles,"copy\n"+string.Join('\n',uris));
        data.Add(listing);
        var storage=TopLevel.GetTopLevel(owner)?.StorageProvider;
        if(storage is not null)
            foreach(var path in full)
            {
                IStorageItem? item=Directory.Exists(path)?await storage.TryGetFolderFromPathAsync(path):await storage.TryGetFileFromPathAsync(path);
                if(item is not null)data.Add(DataTransferItem.CreateFile(item));
            }
        await board.SetDataAsync(data);
        await board.FlushAsync();
    }

    /// PNG bytes as a clipboard image; used by smoke checks and later paste-style actions.
    internal async Task SetImagePngAsync(Window owner,byte[] png)
    {
        var board=Board(owner);
        using var bitmap=new Bitmap(new MemoryStream(png));
        await board.SetBitmapAsync(bitmap);
        await board.FlushAsync();
    }

    internal async Task SetHtmlAsync(Window owner,string html,string? plain=null)
    {
        var board=Board(owner);
        var item=DataTransferItem.Create(Html,html);
        if(plain is not null)item.SetText(plain);
        var data=new DataTransfer();data.Add(item);
        await board.SetDataAsync(data);
        await board.FlushAsync();
    }
}
