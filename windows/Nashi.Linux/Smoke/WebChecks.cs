using Avalonia;
using Avalonia.Media.Imaging;
using Nashi.Core;
namespace Nashi.Linux;

// No network: option building, URL validation and page state only; the info shown in download.png is a fake WebMediaInfo.
internal static partial class SmokeTests
{
    static partial void WebScenes(List<(string Name,Scene Run)> scenes)
    {
        scenes.Add(("web options",(window,directory)=>
        {
            var yt=WebMediaPayload.Parse("https://youtu.be/dQw4w9WgXcQ");var reel=WebMediaPayload.Parse("https://www.instagram.com/reel/AbC123/");var post=WebMediaPayload.Parse("https://www.instagram.com/p/AbC123/");var img=WebMediaPayload.Parse("https://example.com/a.png");var page=WebMediaPayload.Parse("https://example.com/watch");
            var kinds=string.Join("|",WebMediaOptions.Kinds(yt))+";"+string.Join("|",WebMediaOptions.Kinds(reel))+";"+string.Join("|",WebMediaOptions.Kinds(post))+";"+string.Join("|",WebMediaOptions.Kinds(img))+";"+string.Join("|",WebMediaOptions.Kinds(page));
            var kindsOk=kinds=="video|audio|thumbnail;video|audio;photos;image;video|audio|image";
            var heightOk=WebMediaOptions.MaxHeight(0)is null&&WebMediaOptions.MaxHeight(1)==1080&&WebMediaOptions.MaxHeight(2)==720;
            var urlOk=WebMediaOptions.Url("  https://youtu.be/dQw4w9WgXcQ ")=="https://www.youtube.com/watch?v=dQw4w9WgXcQ";
            var rejected=0;foreach(var bad in new[]{"","not a link","ftp://example.com/a","http://localhost/a","https://user:pw@example.com/a","https://printer.local/a"})try{WebMediaOptions.Url(bad);}catch(ArgumentException){rejected++;}
            var toolsOk=WebMediaOptions.NeedsTools("video",page)&&!WebMediaOptions.NeedsTools("image",img)&&!WebMediaOptions.NeedsTools("thumbnail",yt)&&WebMediaOptions.NeedsTools("audio",yt);
            var info=WebDownload.ParseInfo("""{"title":"Fake","formats":[{"height":360,"vcodec":"avc1"},{"height":1080,"vcodec":"avc1"}],"duration":65}""","example.com");
            var infoOk=info.Heights.SequenceEqual([360,1080])&&!info.AudioOnly&&WebMediaOptions.Summary(info).Contains("00:01:05");
            return Task.FromResult<(bool Ok,string? Detail)>((kindsOk&&heightOk&&urlOk&&rejected==6&&toolsOk&&infoOk,$"kinds={kindsOk}, heights={heightOk}, url={urlOk}, rejected={rejected}/6, tools={toolsOk}, info={infoOk}"));
        }));
        scenes.Add(("web link flow",async(window,directory)=>
        {
            window.ShowLibrary(false);
            window.OpenWebLink("https://youtu.be/dQw4w9WgXcQ");
            var opened=window.WebVisible&&window.CurrentPage=="web"&&window.WebLinkText=="https://www.youtube.com/watch?v=dQw4w9WgXcQ"&&window.WebKinds.SequenceEqual(["video","audio","thumbnail"])&&window.WebKind=="video";
            window.OpenWebLink("https://www.instagram.com/p/AbC123/");
            var photos=window.WebKinds.SequenceEqual(["photos"]);
            window.ShowLibrary(false);var home=window.CurrentPage=="home"&&!window.WebVisible;
            await Task.CompletedTask;
            return(opened&&photos&&home,$"opened={opened}, photos={photos}, back={home}");
        }));
        scenes.Add(("render download.png",async(window,directory)=>
        {
            window.OpenWebLink("https://youtu.be/dQw4w9WgXcQ");
            await Task.Delay(300);window.ApplyWebInfo(new WebMediaInfo("A very fine demonstration video","www.youtube.com",212,[360,720,1080],false));
            await Task.Delay(500);window.UpdateLayout();
            var size=new PixelSize(Math.Max(1,(int)window.Bounds.Width),Math.Max(1,(int)window.Bounds.Height));
            var path=Path.Combine(directory,"download.png");
            using(var bmp=new RenderTargetBitmap(size,new Vector(96,96))){bmp.Render(window);bmp.Save(path,new PngBitmapEncoderOptions());}
            var visible=window.WebVisible;window.ShowLibrary(false);
            return(File.Exists(path)&&new FileInfo(path).Length>0&&visible,$"visible={visible}, {size.Width}x{size.Height}");
        }));
    }
}
