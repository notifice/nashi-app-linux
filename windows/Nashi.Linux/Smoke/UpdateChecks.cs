using System.Net;
using Nashi.Linux.Platform;
using Nashi.Linux.Platform.Linux;
namespace Nashi.Linux;

internal static partial class SmokeTests
{
    private sealed class FakeRelease(Func<HttpRequestMessage,HttpResponseMessage> reply):HttpMessageHandler
    {
        public string? UserAgent;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {UserAgent=request.Headers.UserAgent.ToString();return Task.FromResult(reply(request));}
    }
    private static HttpResponseMessage Json(string body,HttpStatusCode code=HttpStatusCode.OK)=>new(code){Content=new StringContent(body)};
    private static string Release(string tag)=>"{\"tag_name\":\""+tag+"\",\"html_url\":\"https://github.com/"+ReleaseNotifier.Repository+"/releases/tag/"+tag+"\"}";
    static partial void UpdateScenes(List<(string Name,Scene Run)> scenes)
    {
        scenes.Add(("update checks",async(window,directory)=>
        {
            var current=new ReleaseNotifier(new FakeRelease(_=>Json("{}"))).Current;
            var next=new Version(current.Major,current.Minor,current.Build+1).ToString();
            async Task<AvailableUpdate?> Check(FakeRelease fake)=>await new ReleaseNotifier(fake).CheckAsync(CancellationToken.None);
            var agent=new FakeRelease(_=>Json(Release("v"+next)));
            var newer=await Check(agent);
            var same=await Check(new(_=>Json(Release("v"+current.ToString(3)))));
            var older=await Check(new(_=>Json(Release("v0.0.1"))));
            var missing=await Check(new(_=>Json("{\"message\":\"Not Found\"}",HttpStatusCode.NotFound)));
            var limited=await Check(new(_=>Json("{}",HttpStatusCode.Forbidden)));
            var malformed=await Check(new(_=>Json("<html>not json")));
            var noFields=await Check(new(_=>Json("{\"tag_name\":\"v99.0.0\"}")));
            var badTag=await Check(new(_=>Json(Release("nightly"))));
            var foreign=await Check(new(_=>Json("{\"tag_name\":\"v99.0.0\",\"html_url\":\"https://example.com/x\"}")));
            var failed=await Check(new(_=>throw new HttpRequestException("offline")));
            var staged=false;
            try{await new ReleaseNotifier(agent).StageAsync(new("1.0.0","https://github.com"),new Progress<Nashi.Core.OperationProgress>(),CancellationToken.None);}catch(NotSupportedException){staged=true;}
            var ua=agent.UserAgent=="Nashi-Linux/"+current.ToString(3);
            window.OfferUpdate(new("99.0.0","https://github.com/"+ReleaseNotifier.Repository+"/releases/tag/v99.0.0"));
            await Task.Delay(200);
            var banner=window.UpdateBannerVisible;
            var ok=newer is not null&&newer.Version==next&&newer.Url.EndsWith("/releases/tag/v"+next)&&same is null&&older is null&&missing is null&&limited is null&&malformed is null&&noFields is null&&badTag is null&&foreign is null&&failed is null&&staged&&ua&&banner&&!new ReleaseNotifier(agent).SelfUpdateSupported;
            return(ok,$"newer={newer?.Version} same={same} older={older} 404={missing} 403={limited} malformed={malformed} noFields={noFields} badTag={badTag} foreign={foreign} offline={failed} staged={staged} ua={agent.UserAgent} banner={banner}");
        }));
    }
}
