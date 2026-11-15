using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nashi.Core;
using Nashi.Linux.Platform;
using Scrubber=Nashi.Linux.Platform.Linux.FrameScrubPlayer;
namespace Nashi.Linux;

// Port of Nashi.Windows/MediaEditorOptions.cs plus the media parts of Nashi.Windows/ToolEditor.cs (player controls, range fields, keys).
// The preview is a frame-scrub player: stills from ffmpeg, no sound. Every media control and option is created here through the two ToolEditor hooks.
internal sealed partial class ToolEditor
{
    private readonly CheckBox trimSilence=new(){Content="Remove silence at the beginning and end",Margin=new(4,12,4,12)},joinClips=new(){Content="Join retained clips into one video",Margin=new(4,12,4,12)};
    private readonly ComboBox visualization=new(){ItemsSource=new[]{"waveform","spectrum","bars","image"},SelectedIndex=0},redactMode=new(){ItemsSource=new[]{"solid","blur","pixelate"},SelectedIndex=0},bleepMode=new(){ItemsSource=new[]{"bleep","mute"},SelectedIndex=0};
    private readonly List<MediaRange> bleepRanges=[];private readonly List<MediaRegion> videoRegions=[];private readonly ListBox rangesList=new(){MaxHeight=140};private string? visualImage;private bool playingRange;private double rangePlaybackEnd;
    private IMediaPlayer? player;private bool playing,syncingScrubber;private double playPosition,duration=1;
    private readonly Slider scrubber=new(){Minimum=0,Margin=new(4,12,4,8)};
    private readonly RangeTimeline timeline=new();private readonly TextBlock time=new(){Margin=new(4,6,4,10),TextWrapping=TextWrapping.Wrap};
    private Action? resetMedia;
    // Fields show milliseconds, so the default End can round up to half a millisecond past ffprobe's duration.
    // MediaEngine allows the same slack; it gets the clamped End.
    private const double RangeSlack=.001;
    // MediaEngine.TimeRange also refuses ranges shorter than 50 ms. Both round the length.
    private const double MinRange=.05;

    partial void BuildMediaOptions()
    {
        if(media is null)return;
        duration=Math.Max(0,media.Duration);var ranged=tool is "trimVideo" or "trimAudio" or "redactAudio";
        BuildMediaPreview(ranged);
        L10n.Choices(visualization);L10n.Choices(redactMode);L10n.Choices(bleepMode);
        if(tool=="redactVideo")
        {
            options.Children.Add(L10n.Label(new TextBlock{Text="Drag a region to cover, then Add selected area. The cover lasts from Start to End.",TextWrapping=TextWrapping.Wrap,Margin=new(4,4,4,16)}));
            Field("X",0);Field("Y",0);Field("Width",bitmap?.PixelSize.Width??media.Video?.Width??1280);Field("Height",bitmap?.PixelSize.Height??media.Video?.Height??720);
        }
        if(ranged||tool=="redactVideo"){Field("Start",0);Field("End",duration);foreach(var name in new[]{"Start","End"})fields[name].LostFocus+=(_,_)=>{try{SyncTimeline();}catch(ArgumentException){}};}
        if(tool is "trimAudio" or "trimVideo")options.Children.Add(L10n.Content(trimSilence));
        if(tool=="changeVideoSpeed")Field("Speed",1);
        if(tool=="splitVideo"){Field("Number of clips",2);TextField("Split points (seconds; separated by semicolons)","");TextField("Remove clips (numbers; separated by semicolons)","");options.Children.Add(L10n.Content(joinClips));}
        if(tool=="videoSnapshots")TextField("Times (seconds)","0");
        if(tool=="normalizeAudio"){Field("Target LUFS",-16);Field("True peak dB",-1.5);Field("Loudness range",11);}
        if(tool=="audioChannels"){Field("Channels",2);Field("Left volume",1);Field("Right volume",1);}
        if(tool=="audioToVideo")
        {
            Field("Width",1280);Field("Height",720);
            options.Children.Add(L10n.Label(new TextBlock{Text="Visualization"}));options.Children.Add(visualization);
            options.Children.Add(Ui.Button("Choose visualization image…",ChooseVisualizationImage));
        }
        if(tool=="redactAudio")
        {
            options.Children.Add(L10n.Label(new TextBlock{Text="Replace selected audio with"}));options.Children.Add(bleepMode);
            options.Children.Add(Ui.Button("Add selected range",()=>{try{var (a,b)=ReadPlaybackRange("Choose a valid range first.");bleepRanges.Add(new(a,b));ReloadRanges();}catch(ArgumentException error){Problem(error.Message,"Check selected range");}}));
            options.Children.Add(rangesList);options.Children.Add(Ui.Button("Remove selected range",()=>{if(rangesList.SelectedIndex is var i&&i>=0&&i<bleepRanges.Count){bleepRanges.RemoveAt(i);ReloadRanges();}}));
        }
        if(tool=="redactVideo")
        {
            options.Children.Add(L10n.Label(new TextBlock{Text="Cover style"}));options.Children.Add(redactMode);
            options.Children.Add(Ui.Button("Add selected area",()=>{try{AddVideoRegion();}catch(ArgumentException error){Problem(error.Message,"Check selected area");}}));
            options.Children.Add(rangesList);options.Children.Add(Ui.Button("Remove selected area",RemoveVideoRegion));
        }
        resetMedia=ResetMedia;
        Opened+=async(_,_)=>{await OpenPlayer();if(ranged)await LoadTimelineArtwork();};
        Closed+=(_,_)=>{try{player?.Dispose();}catch(Exception){}previewLifetime.Cancel();DeletePreviewFiles();};
    }

    // The frame-scrub player sits in the preview column; crop and redact tools already draw the video frame on their canvas, so the player feeds frames to it instead.
    private void BuildMediaPreview(bool ranged)
    {
        var canvasFrames=bitmap is not null;
        if(Services.MediaPlayer is {} factory)
        {
            player=factory();
            if(player is Scrubber scrub&&canvasFrames)scrub.FrameRendered+=frame=>{if(drawing.Children.Count>0&&drawing.Children[0] is Image shown)shown.Source=frame;};
            if(!canvasFrames)previewStack.Children.Add(new Border{Height=260,CornerRadius=new(12),ClipToBounds=true,Child=player.View});
            player.PositionChanged+=OnPlayerPosition;player.Ended+=()=>{playing=false;playingRange=false;};
            player.Failed+=message=>time.Text=L10n.Text("Preview unavailable: ")+message;
        }
        else if(!canvasFrames)previewStack.Children.Add(L10n.Label(new TextBlock{Text="Install ffmpeg (Set up media tools) to preview frames here. Export uses your original file.",TextWrapping=TextWrapping.Wrap,Margin=new(0,20,0,20)}));
        if(ranged)
        {
            timeline.Set(duration,0,duration);timeline.FineStep=FineSeekStep;previewStack.Children.Add(timeline);
            timeline.Changed+=(a,b)=>
            {
                var valid=double.TryParse(fields["Start"].Text,NumberStyles.Float,CultureInfo.CurrentCulture,out var previousStart)&&double.IsFinite(previousStart);
                var moved=!valid||Math.Abs(a-previousStart)>.0001;
                fields["Start"].Text=Number(a);fields["End"].Text=Number(b);Seek(moved?a:b);
            };
            timeline.PlayheadChanged+=seconds=>Seek(seconds);timeline.PlayPauseRequested+=TogglePlayback;
        }
        var controls=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6};
        controls.Children.Add(Ui.Button("−1s",()=>Seek(playPosition-1)));controls.Children.Add(Ui.Button("▶ / Ⅱ",TogglePlayback));controls.Children.Add(Ui.Button("+1s",()=>Seek(playPosition+1)));
        previewStack.Children.Add(controls);previewStack.Children.Add(time);
        if(media!.Video is null&&tool is "normalizeAudio" or "audioChannels" or "redactAudio"||tool is "redactVideo" or "splitVideo" or "audioToVideo")
        {
            var comparison=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6};comparison.Children.Add(Ui.Button("Original",()=>_=PlayOriginalPreview()));
            processedButton=Ui.Button(tool=="splitVideo"?"Play Result":tool=="audioToVideo"?"Preview Visualization":"Preview Processed",()=>_=PreviewProcessed());comparison.Children.Add(processedButton);previewStack.Children.Add(comparison);
        }
        scrubber.Maximum=Math.Max(.001,duration);Avalonia.Controls.ToolTip.SetTip(scrubber,L10n.Text("Preview position"));
        scrubber.PropertyChanged+=(_,e)=>{if(e.Property==RangeBase.ValueProperty&&!syncingScrubber)Seek(scrubber.Value);};previewStack.Children.Add(scrubber);
        if(ranged&&tool!="redactVideo")previewStack.Children.Add(Ui.Button("Play selected range",PlayRange));
        previewStack.Children.Add(L10n.Label(new TextBlock{Text="Preview shows still frames from ffmpeg, without sound. Space play/pause · ←/→ 1s · Shift+←/→ 1 frame (video), 0.1s (audio) · I/O set range · Enter save",TextWrapping=TextWrapping.Wrap,FontSize=12,Foreground=Nashi.Linux.Theme.Brush("Muted")}));
        ShowPosition();
    }

    private async void ChooseVisualizationImage()
    {
        if(Services.Files is null)return;
        try{var picked=await Services.Files.OpenFilesAsync(this,L10n.Text("Choose visualization image…"),false,["png","jpg","bmp","webp"]);if(picked.Count>0){visualImage=picked[0];visualization.SelectedItem="image";}}
        catch(Exception error){Problem(error.Message,"Check your settings");}
    }
    private void Problem(string message,string title)
    {
        if(throwOnError)throw new ArgumentException(message);
        _=Services.Dialogs.InfoAsync(this,L10n.Text(message),L10n.Text(title));
    }
    private void ReloadRanges(){rangesList.ItemsSource=null;rangesList.ItemsSource=tool=="redactAudio"?bleepRanges.Select((r,i)=>$"{i+1} · {r.Start:0.###}–{r.End:0.###} s").ToArray():videoRegions.Select((r,i)=>$"{i+1} · {r.Width:0}×{r.Height:0} · {r.Start:0.###}–{r.End:0.###} s").ToArray();}
    // The pending drag becomes a cover: it turns black, and the next drag starts a new selection. Canvas rectangles pair with videoRegions by position.
    private void AddVideoRegion()
    {
        var (a,b)=ReadPlaybackRange("Choose a valid range first.");var w=Value("Width");var h=Value("Height");
        if(selection is null||w<2||h<2)throw new ArgumentException("Drag an area first.");
        videoRegions.Add(new(Value("X"),Value("Y"),w,h,a,b));selection.Fill=Brushes.Black;selection=null;ReloadRanges();
    }
    private void RemoveVideoRegion()
    {
        var i=rangesList.SelectedIndex;if(i<0||i>=videoRegions.Count)return;
        videoRegions.RemoveAt(i);var shape=drawing.Children.OfType<Rectangle>().ElementAtOrDefault(i);if(shape is not null)drawing.Children.Remove(shape);ReloadRanges();
    }
    private void ResetMedia()
    {
        if(!processingPreview){if(processedPlayback)_=PlayOriginalPreview();player?.Pause();playing=false;Seek(0);}
        foreach(var choice in new[]{visualization,redactMode,bleepMode})choice.SelectedIndex=0;
        trimSilence.IsChecked=joinClips.IsChecked=false;visualImage=null;bleepRanges.Clear();videoRegions.Clear();ReloadRanges();
        if(timeline.Parent is not null)timeline.Set(duration,0,duration);
    }

    private double RangeEnd(){var end=Value("End",duration);return duration>0?Math.Min(end,duration):end;}
    private (double Start,double End) ReadPlaybackRange(string message="Start must be before End, within the media duration.")
    {
        var a=Value("Start");var b=Value("End",duration);var end=RangeEnd();
        if(a<0||Math.Round(end-a,6)<MinRange||duration>0&&b>duration+RangeSlack)throw new ArgumentException(message);
        return(a,end);
    }
    private void SyncTimeline(){if(timeline.Parent is not null&&fields.ContainsKey("Start")&&fields.ContainsKey("End"))timeline.Set(duration,Value("Start"),Value("End",duration));}
    private double FineSeekStep=>media?.Video is {} video?1/Math.Max(1,video.FrameRate):.1;

    // Player plumbing. playPosition is the editor's own clock; the player reports back through PositionChanged.
    private void OnPlayerPosition()
    {
        if(player is null)return;
        playPosition=player.Position.TotalSeconds;ShowPosition();
        if(playingRange&&playPosition>=rangePlaybackEnd){player.Pause();playing=false;playingRange=false;}
    }
    private void ShowPosition()
    {
        syncingScrubber=true;scrubber.Value=Math.Clamp(playPosition,0,scrubber.Maximum);syncingScrubber=false;timeline.SetPlayhead(playPosition);
        time.Text=$"{TimeSpan.FromSeconds(playPosition):mm\\:ss\\.fff} / {TimeSpan.FromSeconds(duration):mm\\:ss\\.fff}";
    }
    private void Seek(double seconds)
    {
        playingRange=false;playPosition=Math.Clamp(seconds,0,duration);
        if(player is not null)player.Position=TimeSpan.FromSeconds(playPosition);
        ShowPosition();
    }
    private void TogglePlayback()
    {
        playingRange=false;if(player is null)return;
        if(playing){player.Pause();playing=false;}
        else{if(playPosition>=duration-.001)Seek(0);player.Play();playing=true;}
    }
    private void PlayRange()
    {
        try{var (a,b)=ReadPlaybackRange();Seek(a);rangePlaybackEnd=b;playingRange=true;player?.Play();playing=player is not null;}
        catch(ArgumentException error){player?.Pause();playing=false;playingRange=false;time.Text=L10n.Text(error.Message);}
    }
    // Space, arrows, I and O drive the preview unless a text box, list or button has the keys.
    private bool HandleMediaKey(KeyEventArgs e,object? focused)
    {
        if(media is null||e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift)||focused is TextBox or Button or ComboBox or Slider or ListBox or ListBoxItem or CheckBox or RangeTimeline)return false;
        var fine=e.KeyModifiers==KeyModifiers.Shift;
        try
        {
            switch(e.Key)
            {
                case Key.Space:if(fine&&timeline.Parent is not null)PlayRange();else TogglePlayback();break;
                case Key.Left:Seek(playPosition-(fine?FineSeekStep:1));break;
                case Key.Right:Seek(playPosition+(fine?FineSeekStep:1));break;
                case Key.I:if(fields.TryGetValue("Start",out var start)&&playPosition<Value("End",duration))start.Text=Number(playPosition);SyncTimeline();break;
                case Key.O:if(fields.TryGetValue("End",out var end)&&playPosition>Value("Start"))end.Text=Number(playPosition);SyncTimeline();break;
                default:return false;
            }
        }
        catch(ArgumentException error){time.Text=L10n.Text(error.Message);}
        return true;
    }

    partial void ReadMediaOptions(ref MediaOptions mediaOptions)
    {
        if(media is null)return;
        if(tool is "trimVideo" or "trimAudio"||tool=="redactAudio"&&bleepRanges.Count==0)ReadPlaybackRange();
        if(tool=="redactVideo"&&videoRegions.Count==0){try{AddVideoRegion();}catch(ArgumentException){}if(videoRegions.Count==0)throw new ArgumentException("Drag an area first.");}
        var x=Value("X");var y=Value("Y");var w=Value("Width");var h=Value("Height");
        double[] Numbers(string name)=>fields.TryGetValue(name,out var field)&&!string.IsNullOrWhiteSpace(field.Text)?field.Text.Split(';',StringSplitOptions.RemoveEmptyEntries).Select(s=>NumericInput.ParseFinite(s.Trim())).ToArray():[];
        var ranged=fields.ContainsKey("End");
        var original=new MediaOptions{Start=Value("Start"),End=ranged?RangeEnd():null,Quality=Value("Quality (%)",80)/100,Speed=Value("Speed",1),Width=fields.ContainsKey("Width")?(int)w:null,Height=fields.ContainsKey("Height")?(int)h:null,Channels=(int)Value("Channels",2),LeftVolume=Value("Left volume",1),RightVolume=Value("Right volume",1),EqualClips=(int)Value("Number of clips",2),TargetLoudness=Value("Target LUFS",-16),TruePeak=Value("True peak dB",-1.5),Regions=tool is "cropVideo"?[new(x,y,w,h,Value("Start"),null)]:[],AudioRedactions=tool=="redactAudio"?[new(Value("Start"),RangeEnd())]:[],SnapshotTimes=tool=="videoSnapshots"?fields["Times (seconds)"].Text!.Split(';',StringSplitOptions.RemoveEmptyEntries).Select(s=>NumericInput.ParseFinite(s)).ToArray():[0]};
        var target=Value("Target size (KB; 0 = auto)");var reduction=Value("Reduction (%; 0 = auto)");var maxHeight=(int)Value("Maximum video height (0 = original)");
        mediaOptions=original with{AudioBitrate=(int)Value("Audio bitrate (kbps)",256),SampleDepth=sampleDepth.SelectedItem as string??"matchSource",GifFrameRate=(int)Value("GIF frames per second",15),GifWidth=(int)Value("GIF width",960),Height=tool=="convert"&&maxHeight>0?maxHeight:original.Height,PreserveMetadata=preserveMetadata.IsChecked==true,TargetBytes=target>0?(long)(target*1024):null,ReductionPercent=reduction>0?reduction:null,TrimSilence=trimSilence.IsChecked==true,LoudnessRange=Value("Loudness range",11),SplitPoints=Numbers("Split points (seconds; separated by semicolons)"),DiscardedClips=Numbers("Remove clips (numbers; separated by semicolons)").Select(n=>(int)n-1).ToArray(),JoinSplitClips=joinClips.IsChecked==true,Visualization=visualization.SelectedItem as string??"waveform",VisualizationImage=visualImage,RedactionMode=tool=="redactAudio"?bleepMode.SelectedItem as string??"bleep":redactMode.SelectedItem as string??"solid",AudioRedactions=tool=="redactAudio"&&bleepRanges.Count>0?bleepRanges.ToArray():original.AudioRedactions,Regions=tool=="redactVideo"&&videoRegions.Count>0?videoRegions.ToArray():original.Regions};
    }

    // Test seams for Smoke/MediaChecks.cs.
    internal RangeTimeline TimelineForTest=>timeline;
    internal IMediaPlayer? PlayerForTest=>player;
    internal void SetRangeForTest(double start,double end){fields["Start"].Text=Number(start);fields["End"].Text=Number(end);SyncTimeline();}
}
