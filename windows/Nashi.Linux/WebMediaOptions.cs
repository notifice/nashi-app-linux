using Nashi.Core;
namespace Nashi.Linux;

// Pure option helpers for the download page, ported from the choices in Nashi.Windows/WebMediaOptions.cs and MainWindow.Settings.cs.
internal static class WebMediaOptions
{
    internal static readonly string[] QualityLabels=["Best available","Up to 1080p","Up to 720p"];
    /// Formats the download page offers for a recognised link.
    internal static string[] Kinds(WebMediaPayload? payload)=>payload?.Kind switch{"youtube"=>["video","audio","thumbnail"],"instagramPhotos"=>["photos"],"instagramReel"=>["video","audio"],"image"=>["image"],_=>["video","audio","image"]};
    /// Maximum video height for a quality choice; null means best available.
    internal static int? MaxHeight(int qualityIndex)=>qualityIndex==1?1080:qualityIndex==2?720:null;
    /// Throws ArgumentException with a user-facing message unless the text holds one public http(s) link; returns the link to download.
    internal static string Url(string? text)=>WebDownload.Validate(WebMediaPayload.Parse(text)?.Url??text?.Trim()??"").AbsoluteUri;
    /// Image links and YouTube thumbnails work without yt-dlp and ffmpeg.
    internal static bool NeedsTools(string kind,WebMediaPayload? payload)=>kind!="image"&&payload?.Kind!="image"&&!(kind=="thumbnail"&&payload?.Kind=="youtube");
    internal static string Summary(WebMediaInfo info)=>info.Title+"\n"+info.Site+(info.Duration is {} seconds?" · "+TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss"):"")+(info.Heights.Length>0?" · up to "+info.Heights.Max()+"p":"");
}
