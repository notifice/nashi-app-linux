using Nashi.Core;
using Nashi.Engine;
namespace Nashi.Linux.Platform.Linux;

// Thumbnails for image files only; PDF first pages arrive with the PDF renderer. Cached by path, mtime, size and requested edge.
internal sealed class FileArtwork:IFileArtwork
{
    private const int Capacity=64;
    private readonly record struct Key(string Path,long Ticks,long Length,int Size);
    private readonly object gate=new();
    private readonly Dictionary<Key,LinkedListNode<(Key Key,PixelImage? Image)>> index=[];
    private readonly LinkedList<(Key Key,PixelImage? Image)> order=new();

    public Task<PixelImage?> ThumbnailAsync(string path,int size,CancellationToken token)
    {
        if(size<1||!FeatureCatalog.IsImage(path)||!File.Exists(path))return Task.FromResult<PixelImage?>(null);
        var info=new FileInfo(path);var key=new Key(info.FullName,info.LastWriteTimeUtc.Ticks,info.Length,size);
        lock(gate)
            if(index.TryGetValue(key,out var hit)){order.Remove(hit);order.AddFirst(hit);return Task.FromResult(hit.Value.Image);}
        return Task.Run<PixelImage?>(()=>
        {
            token.ThrowIfCancellationRequested();
            PixelImage? thumbnail;
            try{thumbnail=Shrink(ImageEngine.Load(path),size);}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or ImageMagick.MagickException){thumbnail=null;}
            lock(gate)
                if(!index.ContainsKey(key))
                {
                    index[key]=order.AddFirst((key,thumbnail));
                    while(order.Count>Capacity){var last=order.Last!;order.RemoveLast();index.Remove(last.Value.Key);}
                }
            return thumbnail;
        },token);
    }

    // Area average so small icons stay smooth; fits the longest edge into size and never enlarges.
    private static PixelImage Shrink(PixelImage source,int size)
    {
        var scale=Math.Min(1.0,(double)size/Math.Max(source.Width,source.Height));
        if(scale>=1)return source;
        var width=Math.Max(1,(int)Math.Round(source.Width*scale));var height=Math.Max(1,(int)Math.Round(source.Height*scale));
        var output=new byte[width*height*4];
        for(var y=0;y<height;y++)
        {
            var y0=y*source.Height/height;var y1=Math.Max(y0+1,(y+1)*source.Height/height);
            for(var x=0;x<width;x++)
            {
                var x0=x*source.Width/width;var x1=Math.Max(x0+1,(x+1)*source.Width/width);
                long b=0,g=0,r=0,a=0,count=0;
                for(var sy=y0;sy<y1;sy++)for(var sx=x0;sx<x1;sx++)
                {
                    var i=(sy*source.Width+sx)*4;var alpha=source.Bgra[i+3];
                    b+=source.Bgra[i]*alpha;g+=source.Bgra[i+1]*alpha;r+=source.Bgra[i+2]*alpha;a+=alpha;count++;
                }
                var o=(y*width+x)*4;
                if(a>0){output[o]=(byte)(b/a);output[o+1]=(byte)(g/a);output[o+2]=(byte)(r/a);}
                output[o+3]=(byte)(a/count);
            }
        }
        return new PixelImage(width,height,output,source.Dpi);
    }
}
