using System.Net;
using System.Text.Json;
using Nashi.Core;
namespace Nashi.Linux.Platform.Linux;

// Linux has no self-update: this only asks GitHub for the newest release of the Linux fork and reports it.
internal sealed class ReleaseNotifier:IUpdateInstaller
{
    internal const string Repository="notifice/nashi-app-linux";
    internal const string LatestReleaseUrl="https://api.github.com/repos/"+Repository+"/releases/latest";
    private readonly HttpClient http;
    public Version Current{get;}=Normalize(typeof(ReleaseNotifier).Assembly.GetName().Version??new Version(0,0));
    public bool SelfUpdateSupported=>false;
    public ReleaseNotifier():this(new HttpClientHandler()){}
    internal ReleaseNotifier(HttpMessageHandler handler)
    {
        http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(10)};
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Nashi-Linux/"+Current.ToString(3));
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }
    internal static Version Normalize(Version v)=>new(Math.Max(v.Major,0),Math.Max(v.Minor,0),Math.Max(v.Build,0),Math.Max(v.Revision,0));
    /// True when the last check could not reach or read the release feed (as opposed to "no newer release").
    internal bool LastCheckFailed{get;private set;}
    public async Task<AvailableUpdate?> CheckAsync(CancellationToken token)
    {
        LastCheckFailed=false;
        try
        {
            using var response=await http.GetAsync(LatestReleaseUrl,token);
            // 404 means the fork has no releases yet; 403/429 is the anonymous rate limit. Neither is worth bothering anyone about.
            if(response.StatusCode!=HttpStatusCode.OK){if(response.StatusCode!=HttpStatusCode.NotFound){LastCheckFailed=true;Console.Error.WriteLine("Release check: HTTP "+(int)response.StatusCode);}return null;}
            using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var root=json.RootElement;
            if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("tag_name",out var tagElement)||!root.TryGetProperty("html_url",out var urlElement))return null;
            var tag=tagElement.ValueKind==JsonValueKind.String?tagElement.GetString()?.Trim():null;var url=urlElement.ValueKind==JsonValueKind.String?urlElement.GetString():null;
            if(string.IsNullOrEmpty(tag)||string.IsNullOrEmpty(url))return null;
            if(tag[0] is 'v' or 'V')tag=tag[1..];
            var cut=tag.IndexOfAny(['-','+']);if(cut>=0)tag=tag[..cut];
            if(!Version.TryParse(tag,out var version))return null;
            // Only ever hand a github.com page to the shell.
            if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="github.com")return null;
            return Normalize(version)>Current?new AvailableUpdate(version.ToString(),url):null;
        }
        catch(Exception error)when(error is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or IOException)
        {LastCheckFailed=true;
            if(!token.IsCancellationRequested)Console.Error.WriteLine("Release check failed: "+error.Message);
            return null;
        }
    }
    public Task StageAsync(AvailableUpdate update,IProgress<OperationProgress> progress,CancellationToken token)=>throw new NotSupportedException("Nashi for Linux does not update itself.");
}
