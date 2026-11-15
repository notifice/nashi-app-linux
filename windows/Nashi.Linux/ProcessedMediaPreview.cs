using Avalonia.Controls;
using Nashi.Core;
namespace Nashi.Linux;

// Port of Nashi.Windows/ProcessedMediaPreview.cs: renders the tool's result into a temp folder and shows it in the same player
// (frames for video; the waveform for audio). Export always starts again from the original file.
internal sealed partial class ToolEditor
{
    private Button? processedButton;private bool processingPreview,processedPlayback;
    internal string? ProcessedPreviewPath{get;private set;}
    internal bool ProcessedPlayback=>processedPlayback;
    private async Task PlayOriginalPreview()
    {
        if(processingPreview||media is null||player is null)return;
        processedPlayback=false;drawing.IsEnabled=true;duration=media.Duration;scrubber.Maximum=Math.Max(.001,duration);
        await player.OpenAsync(input,previewLifetime.Token);Seek(0);
    }
    private async Task PreviewProcessed()
    {
        if(processingPreview||player is null)return;
        if(FfmpegPath() is not {} ffmpeg||previewTools is null){time.Text=L10n.Text("Set up media tools to preview the processed result.");return;}
        if(!ReadSettings(false))return;
        processingPreview=true;if(processedButton is not null){processedButton.IsEnabled=false;processedButton.Content=L10n.Text("Preparing…");}time.Text=L10n.Text("Preparing processed preview…");
        try
        {
            var engine=new MediaEngine(ffmpeg,previewTools.Executable("ffprobe"));var settings=tool=="splitVideo"?MediaOptions with{JoinSplitClips=true}:MediaOptions;
            var rendered=(await engine.ToolAsync(tool,[input],PreviewDirectory(),settings,null,previewLifetime.Token))[0];
            var playable=(await engine.ConvertAsync([rendered],media?.Video is null&&tool!="audioToVideo"?"wav":"mp4",PreviewDirectory(),new(){PreserveMetadata=false},null,previewLifetime.Token))[0];
            var info=await engine.InspectAsync(playable,previewLifetime.Token);if(previewLifetime.IsCancellationRequested)return;
            ProcessedPreviewPath=playable;await player.OpenAsync(playable,previewLifetime.Token);duration=info.Duration;scrubber.Maximum=Math.Max(.001,duration);
            processedPlayback=true;drawing.IsEnabled=false;Seek(0);player.Play();playing=true;time.Text=L10n.Text("Processed preview · export uses the original file.");
        }
        catch(OperationCanceledException){}
        catch(Exception error){if(IsVisible)time.Text=L10n.Text("Preview unavailable: ")+error.Message;}
        finally
        {
            processingPreview=false;
            if(processedButton is not null){processedButton.IsEnabled=true;processedButton.Content=tool=="splitVideo"?"Play Result":tool=="audioToVideo"?"Preview Visualization":"Preview Processed";L10n.Content(processedButton);}
        }
    }
}
