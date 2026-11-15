using Nashi.Core;
using Nashi.Engine;
namespace Nashi.Linux;

// Port of the media branch of Nashi.Windows/MainWindow.cs EditAsync: probe the first file, open ToolEditor, run the MediaEngine tool.
// Plain conversions ("convert:fmt", which also covers extracting audio from a video) stay in ConvertWithToastAsync.
internal sealed partial class MainWindow
{
    internal static readonly string[] MediaToolIds=["compress","muteVideo","trimVideo","cropVideo","changeVideoSpeed","videoSnapshots","splitVideo","redactVideo","normalizeAudio","audioToVideo","trimAudio","audioChannels","redactAudio","joinVideos"];
    /// Handles the wheel choices that belong to video and audio. Returns false for choices another flow owns.
    private async Task<bool> RunMediaRouteAsync(ToolChoice choice)
    {
        if(!(Video||Audio)||!MediaToolIds.Contains(choice.Id))return false;
        if(!tools.Ready){await SetupAsync();if(!tools.Ready)return true;}
        var selected=files.ToArray();var engine=new MediaEngine(tools.Executable("ffmpeg"),tools.Executable("ffprobe"));
        MediaInfo info;PixelImage? frame=null;
        SetBusy(true);
        try
        {
            info=await engine.InspectAsync(selected[0]);
            if(choice.Id is "cropVideo" or "redactVideo")
            {
                var temporary=Path.Combine(Path.GetTempPath(),"Nashi-preview-"+Guid.NewGuid().ToString("N")+".png");
                try{await ProcessRunner.RunAsync(tools.Executable("ffmpeg"),["-v","error","-nostdin","-i",selected[0],"-frames:v","1",temporary]);frame=await Task.Run(()=>ImageEngine.Load(temporary));}
                finally{if(File.Exists(temporary))File.Delete(temporary);}
            }
        }
        finally{SetBusy(false);}
        var accepted=await ToolEditor.ShowDetailedAsync(DialogOwner,choice.Id,selected,preferences,title:choice.Title,media:info,frame:frame,tools:tools);
        if(accepted is null)return true;
        var inputs=choice.Id is "cropVideo" or "redactVideo"?[selected[0]]:accepted.OrderedInputs is {Length:>0} ordered?ordered:selected;
        var destination=OutputDirectory(inputs);var options=accepted.Media;
        await RunWithToastAsync(choice.Title,(report,token)=>engine.ToolAsync(choice.Id,inputs,destination,options,report,token));
        return true;
    }
}
