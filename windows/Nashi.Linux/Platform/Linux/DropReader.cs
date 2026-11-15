using Avalonia.Input;
using Avalonia.Platform.Storage;
namespace Nashi.Linux.Platform.Linux;

// Avalonia 12 exposes dropped content as DragEventArgs.DataTransfer (IDataTransfer + DataFormat), not the 11.x e.Data.
internal sealed class DropReader:IDropReader
{
    private static readonly DataFormat<string> Html=DataFormat.CreateStringPlatformFormat("text/html");
    private static readonly DataFormat<string> UriList=DataFormat.CreateStringPlatformFormat("text/uri-list");

    public bool Accepts(DragEventArgs e)
    {
        var d=e.DataTransfer;
        return d.Contains(DataFormat.File)||d.Contains(DataFormat.Text)||d.Contains(DataFormat.Bitmap)||d.Contains(Html)||d.Contains(UriList);
    }

    public Task<TransferContent> ReadAsync(DragEventArgs e)
    {
        var d=e.DataTransfer;
        var files=new List<string>();
        foreach(var item in d.TryGetFiles()??[])
            if(item.TryGetLocalPath() is{Length:>0}p)files.Add(p);
        var text=d.TryGetText();
        var html=d.TryGetValue(Html);
        string? url=null;
        var list=d.TryGetValue(UriList);
        if(list is not null)
            url=list.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(s=>s.Trim()).FirstOrDefault(s=>s.Length>0&&!s.StartsWith('#')&&!s.StartsWith("file:",StringComparison.OrdinalIgnoreCase));
        if(url is null&&text is not null&&Uri.TryCreate(text.Trim(),UriKind.Absolute,out var u)&&(u.Scheme==Uri.UriSchemeHttp||u.Scheme==Uri.UriSchemeHttps))url=u.AbsoluteUri;
        byte[]? png=null;
        if(files.Count==0)
        {
            using var bmp=d.TryGetBitmap();
            if(bmp is not null){using var ms=new MemoryStream();bmp.Save(ms,new Avalonia.Media.Imaging.PngBitmapEncoderOptions());png=ms.ToArray();}
        }
        return Task.FromResult(new TransferContent(files,png,text,html,url));
    }
}
