using Avalonia;
using Avalonia.Media.Imaging;
using Nashi.Core;
using Nashi.Engine;
using Nashi.Linux.Platform;
namespace Nashi.Linux;

internal static partial class SmokeTests
{
    static partial void LibraryScenes(List<(string Name,Scene Run)> scenes)
    {
        scenes.Add(("clipboard text round trip",async(window,directory)=>
        {
            if(Services.Clipboard is null)return(false,"Services.Clipboard is null");
            const string text="Nashi clipboard text — ✓";
            await Services.Clipboard.SetTextAsync(window,text);
            var read=await Services.Clipboard.ReadAsync(window);
            return(read.Text==text&&read.Files.Count==0,$"read {read.Text?.Length} chars, {read.Files.Count} files");
        }));
        scenes.Add(("clipboard files round trip",async(window,directory)=>
        {
            if(Services.Clipboard is null)return(false,"Services.Clipboard is null");
            var a=Path.Combine(directory,"clip-a.txt");var b=Path.Combine(directory,"clip with space-b.txt");
            File.WriteAllText(a,"a");File.WriteAllText(b,"b");
            await Services.Clipboard.SetFilesAsync(window,[a,b]);
            var read=await Services.Clipboard.ReadAsync(window);
            var ok=read.Files.OrderBy(f=>f).SequenceEqual(new[]{a,b}.OrderBy(f=>f));
            return(ok,string.Join(", ",read.Files));
        }));
        scenes.Add(("clipboard import",async(window,directory)=>
        {
            if(Services.Clipboard is null)return(false,"Services.Clipboard is null");
            var file=Path.Combine(directory,"clip-import.txt");File.WriteAllText(file,"import");
            await Services.Clipboard.SetFilesAsync(window,[file]);
            await window.ImportClipboardAsync(record:false);
            var ok=window.SelectedFiles.SequenceEqual([file]);
            return(ok,string.Join(", ",window.SelectedFiles));
        }));
        scenes.Add(("artwork thumbnails",async(window,directory)=>
        {
            if(Services.Artwork is null)return(false,"Services.Artwork is null");
            var png=Path.Combine(directory,"art.png");
            File.WriteAllBytes(png,ImageEngine.PngBytes(new PixelImage(200,100,Enumerable.Range(0,200*100*4).Select(i=>(byte)(i%4==3?255:i%251)).ToArray())));
            var text=Path.Combine(directory,"art.txt");File.WriteAllText(text,"x");
            var thumb=await Services.Artwork.ThumbnailAsync(png,58,CancellationToken.None);
            var again=await Services.Artwork.ThumbnailAsync(png,58,CancellationToken.None);
            var none=await Services.Artwork.ThumbnailAsync(text,58,CancellationToken.None);
            var ok=thumb is{Width:58,Height:29}&&ReferenceEquals(thumb,again)&&none is null;
            return(ok,thumb is null?"null":$"{thumb.Width}x{thumb.Height}, cached={ReferenceEquals(thumb,again)}, text={none is null}");
        }));
        scenes.Add(("render library.png",async(window,directory)=>
        {
            var image=Path.Combine(directory,"art.png");
            if(!File.Exists(image))File.WriteAllBytes(image,ImageEngine.PngBytes(new PixelImage(64,48,Enumerable.Repeat((byte)180,64*48*4).ToArray())));
            var note=Path.Combine(directory,"clip-a.txt");if(!File.Exists(note))File.WriteAllText(note,"a");
            var entries=new[]
            {
                new LibraryEntry(Guid.NewGuid(),DateTimeOffset.Now,"Convert to PNG",[image]),
                new LibraryEntry(Guid.NewGuid(),DateTimeOffset.Now.AddMinutes(-12),"Pasted text",[note]),
                new LibraryEntry(Guid.NewGuid(),DateTimeOffset.Now.AddHours(-3),"Pasted files",[image,note,Path.Combine(directory,"gone.png")]),
            };
            window.ShowLibraryPreview(entries);
            await Task.Delay(500);window.UpdateLayout();
            var size=new PixelSize(Math.Max(1,(int)window.Bounds.Width),Math.Max(1,(int)window.Bounds.Height));
            var path=Path.Combine(directory,"library.png");
            using(var bmp=new RenderTargetBitmap(size,new Vector(96,96))){bmp.Render(window);bmp.Save(path,new PngBitmapEncoderOptions());}
            var rows=window.LibraryRowCount;var visible=window.LibraryVisible;
            window.ShowLibraryPreview(null);window.ShowLibrary(false);
            return(File.Exists(path)&&new FileInfo(path).Length>0&&rows==entries.Length&&visible,$"{rows} rows, visible={visible}, {size.Width}x{size.Height}");
        }));
    }
}
