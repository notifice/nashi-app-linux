using System.Globalization;
using Avalonia.Media.Imaging;
using Nashi.Core;
namespace Nashi.Linux;

// Port of Nashi.Windows/MediaPreviewRuntime.cs. There is no codec proxy: frames come from ffmpeg, which reads what the export reads.
// This file opens the preview in the player and paints the timeline artwork (ten frames, or a waveform for audio).
internal sealed partial class ToolEditor
{
    private readonly CancellationTokenSource previewLifetime=new();private Toolchain? previewTools;private string? previewDirectory;
    internal void ConfigureMediaRuntime(Toolchain tools)=>previewTools=tools;
    private string? FfmpegPath(){try{return previewTools?.Executable("ffmpeg");}catch(FileNotFoundException){return null;}}
    private string PreviewDirectory()=>previewDirectory??=Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),"Nashi-preview-"+Guid.NewGuid().ToString("N"))).FullName;
    private async Task OpenPlayer()
    {
        if(player is null||media is null)return;
        await player.OpenAsync(input,previewLifetime.Token);
        // MediaEngine checks ranges against ffprobe's duration, so the player's value only stands in when ffprobe reported none.
        if(media.Duration<=0&&player.Duration>TimeSpan.Zero)
        {
            duration=player.Duration.TotalSeconds;scrubber.Maximum=duration;
            if(fields.TryGetValue("End",out var end)&&end.Text==defaults["End"])end.Text=defaults["End"]=Number(duration);
            try{SyncTimeline();}catch(ArgumentException){}
        }
        ShowPosition();
    }
    private async Task LoadTimelineArtwork()
    {
        if(FfmpegPath() is not {} ffmpeg||media is null)return;
        try
        {
            if(media.Video is not null)
            {
                var frames=new List<Bitmap>();
                for(var n=0;n<10;n++)
                {
                    previewLifetime.Token.ThrowIfCancellationRequested();var frame=Path.Combine(PreviewDirectory(),$"frame-{n}.png");
                    await ProcessRunner.RunAsync(ffmpeg,["-v","error","-nostdin","-ss",(Math.Max(0,duration-.001)*n/10).ToString(CultureInfo.InvariantCulture),"-i",input,"-frames:v","1","-vf","scale=160:-1",frame],previewLifetime.Token);
                    frames.Add(new Bitmap(frame));
                }
                if(!previewLifetime.IsCancellationRequested)timeline.SetArtwork(frames.ToArray());
            }
            else
            {
                var frame=Path.Combine(PreviewDirectory(),"waveform.png");
                await ProcessRunner.RunAsync(ffmpeg,["-v","error","-nostdin","-i",input,"-filter_complex","aformat=channel_layouts=mono,showwavespic=s=1200x140:colors=0xe5bc59","-frames:v","1",frame],previewLifetime.Token);
                if(!previewLifetime.IsCancellationRequested)timeline.SetArtwork([new Bitmap(frame)]);
            }
        }
        catch(OperationCanceledException){}
        catch(Exception error){if(IsVisible)Avalonia.Controls.ToolTip.SetTip(timeline,"Timeline preview unavailable: "+error.Message);}
    }
    private void DeletePreviewFiles(){if(previewDirectory is null)return;var path=previewDirectory;_ = Task.Run(async()=>{for(var n=0;n<10;n++){try{if(Directory.Exists(path))Directory.Delete(path,true);return;}catch(IOException){await Task.Delay(200);}catch(UnauthorizedAccessException){return;}}});}
}
