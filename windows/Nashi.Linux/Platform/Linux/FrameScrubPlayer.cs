using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Nashi.Core;
namespace Nashi.Linux.Platform.Linux;

// IMediaPlayer without LibVLC: every position is one ffmpeg still frame, so scrubbing works but nothing is audible.
// Play steps the position at about 4 frames per second. Seeks are debounced and a newer request kills the ffmpeg still being rendered.
// Audio-only files show one waveform picture with a playhead line instead of frames.
internal sealed class FrameScrubPlayer:IMediaPlayer
{
    internal const int StepMilliseconds=250;
    private readonly string ffmpeg;private readonly MediaEngine engine;
    private readonly Image image=new(){Stretch=Stretch.Uniform};
    private readonly Rectangle playhead=new(){Width=2,Fill=Brushes.OrangeRed,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Stretch,IsVisible=false,IsHitTestVisible=false};
    private readonly Grid view=new(){Background=new SolidColorBrush(Color.FromRgb(28,30,26)),MinHeight=120};
    private readonly CancellationTokenSource lifetime=new();private CancellationTokenSource? rendering;private readonly DispatcherTimer clock=new(){Interval=TimeSpan.FromMilliseconds(StepMilliseconds)};
    private string? path;private bool hasVideo,playing,disposed;private TimeSpan position,duration;
    internal int FramesRendered{get;private set;}
    /// The frame (or waveform) currently shown.
    internal Bitmap? Frame{get;private set;}
    internal event Action<Bitmap>? FrameRendered;
    public FrameScrubPlayer(string ffmpegPath,string ffprobePath)
    {
        ffmpeg=ffmpegPath;engine=new(ffmpegPath,ffprobePath);view.Children.Add(image);view.Children.Add(playhead);
        view.SizeChanged+=(_,_)=>MovePlayhead();clock.Tick+=(_,_)=>Step();
    }
    public bool HasAudio=>false;
    public Control View=>view;
    public bool IsPlaying=>playing;
    public TimeSpan Duration=>duration;
    public TimeSpan Position
    {
        get=>position;
        set
        {
            if(disposed||path is null)return;
            position=Clamp(value);MovePlayhead();PositionChanged?.Invoke();
            if(hasVideo)Request(playing?0:120);
        }
    }
    public event Action? PositionChanged;
    public event Action? Ended;
    public event Action<string>? Failed;
    private TimeSpan Clamp(TimeSpan value)=>value<TimeSpan.Zero?TimeSpan.Zero:value>duration?duration:value;

    public async Task OpenAsync(string path,CancellationToken token)
    {
        if(disposed)return;Stop();
        try
        {
            using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,lifetime.Token);
            var info=await engine.InspectAsync(path,linked.Token);
            this.path=path;duration=TimeSpan.FromSeconds(Math.Max(0,info.Duration));position=TimeSpan.Zero;hasVideo=info.Video is not null;
            if(!hasVideo&&info.Audio is null)throw new InvalidDataException("This file has no video or audio to preview.");
            playhead.IsVisible=!hasVideo;image.Stretch=hasVideo?Stretch.Uniform:Stretch.Fill;
            var bytes=hasVideo?await RenderFrameAsync(0,linked.Token):await RenderWaveformAsync(path,linked.Token);
            if(linked.IsCancellationRequested||disposed)return;
            Show(bytes);MovePlayhead();PositionChanged?.Invoke();
        }
        catch(OperationCanceledException){}
        catch(Exception error){if(!disposed)Failed?.Invoke(error.Message);}
    }
    public void Play()
    {
        if(disposed||path is null||playing)return;
        if(position>=duration)position=TimeSpan.Zero;
        playing=true;clock.Start();
    }
    public void Pause(){playing=false;clock.Stop();}
    private void Step()
    {
        if(!playing)return;
        position+=TimeSpan.FromMilliseconds(StepMilliseconds);
        if(position>=duration)
        {
            position=duration;playing=false;clock.Stop();
            if(hasVideo)Request(0);MovePlayhead();PositionChanged?.Invoke();Ended?.Invoke();return;
        }
        if(hasVideo)Request(0);MovePlayhead();PositionChanged?.Invoke();
    }
    private void Stop(){Pause();rendering?.Cancel();}
    private void MovePlayhead(){if(!playhead.IsVisible)return;var fraction=duration>TimeSpan.Zero?position/duration:0;playhead.Margin=new(Math.Max(0,view.Bounds.Width-2)*fraction,0,0,0);}

    // A newer request cancels the previous one, which kills its ffmpeg; the delay debounces rapid scrubbing.
    private void Request(int delay)
    {
        rendering?.Cancel();
        var source=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);rendering=source;
        _=RenderAsync(position.TotalSeconds,delay,source);
    }
    private async Task RenderAsync(double seconds,int delay,CancellationTokenSource source)
    {
        var token=source.Token;
        try
        {
            if(delay>0)await Task.Delay(delay,token);
            var bytes=await RenderFrameAsync(seconds,token);token.ThrowIfCancellationRequested();Show(bytes);
        }
        catch(OperationCanceledException){}
        catch(Exception error){if(!disposed&&!token.IsCancellationRequested)Failed?.Invoke(error.Message);}
        finally{if(ReferenceEquals(rendering,source))rendering=null;source.Dispose();}
    }
    private void Show(byte[] png)
    {
        using var stream=new MemoryStream(png);var bitmap=new Bitmap(stream);
        Frame=bitmap;image.Source=bitmap;FramesRendered++;FrameRendered?.Invoke(bitmap);
    }

    /// One PNG still at <paramref name="seconds"/>, at most 960 px wide. The seek stops a little before the end, where a last frame still exists.
    internal Task<byte[]> RenderFrameAsync(double seconds,CancellationToken token)
    {
        var at=Math.Clamp(seconds,0,Math.Max(0,duration.TotalSeconds-.1));
        return PngAsync(["-v","error","-nostdin","-ss",at.ToString("0.###",CultureInfo.InvariantCulture),"-i",path!,"-frames:v","1","-vf","scale=w='min(960,iw)':h=-2","-f","image2pipe","-vcodec","png","-"],token);
    }
    private Task<byte[]> RenderWaveformAsync(string file,CancellationToken token)=>
        PngAsync(["-v","error","-nostdin","-i",file,"-filter_complex","aformat=channel_layouts=mono,showwavespic=s=1200x240:colors=0xe5bc59","-frames:v","1","-f","image2pipe","-vcodec","png","-"],token);

    private async Task<byte[]> PngAsync(string[] arguments,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var start=new ProcessStartInfo(ffmpeg){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
        foreach(var argument in arguments)start.ArgumentList.Add(argument);
        using var process=Process.Start(start)??throw new IOException("ffmpeg did not start.");
        using var registration=token.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(Exception){}});
        var error=process.StandardError.ReadToEndAsync();
        using var buffer=new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(buffer);await process.WaitForExitAsync();
        token.ThrowIfCancellationRequested();
        if(buffer.Length==0)throw new IOException("ffmpeg produced no frame. "+(await error).Trim());
        return buffer.ToArray();
    }

    public void Dispose()
    {
        if(disposed)return;disposed=true;playing=false;clock.Stop();
        try{lifetime.Cancel();}catch(ObjectDisposedException){}
    }
}
