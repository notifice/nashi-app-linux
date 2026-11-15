using Avalonia.Input;
using Nashi.Core;
using Nashi.Engine;
using Nashi.Linux.Platform;
using Scrubber=Nashi.Linux.Platform.Linux.FrameScrubPlayer;
namespace Nashi.Linux;

// P3-A: the frame-scrub player and the media editor. Skipped with a clear detail when ffmpeg or ffprobe is missing.
internal static partial class SmokeTests
{
    static partial void MediaScenes(List<(string Name,Scene Run)> scenes)
    {
        var tools=Nashi.Linux.Platform.ToolPaths.CreateToolchain();
        string ffmpeg,ffprobe;
        try{ffmpeg=tools.Executable("ffmpeg");ffprobe=tools.Executable("ffprobe");}
        catch(FileNotFoundException){scenes.Add(("media (skipped)",(_,_)=>Task.FromResult<(bool,string?)>((true,"skipped: ffmpeg or ffprobe was not found on PATH"))));return;}
        var engine=new MediaEngine(ffmpeg,ffprobe);

        scenes.Add(("media frame scrub",async(window,directory)=>
        {
            var file=await MediaFixture(ffmpeg,directory);
            if(Services.MediaPlayer is not {} factory)return(false,"Services.MediaPlayer is null although ffmpeg exists");
            using var player=factory();
            await player.OpenAsync(file,CancellationToken.None);
            var scrub=(Scrubber)player;var opened=scrub.FramesRendered;
            player.Position=TimeSpan.FromSeconds(1.5);
            for(var n=0;n<100&&scrub.FramesRendered<=opened;n++)await Task.Delay(50);
            // Rapid seeks: only the last one has to land, and nothing may fail.
            string? failure=null;player.Failed+=message=>failure=message;
            foreach(var second in new[]{.2,.9,2.4,1.5})player.Position=TimeSpan.FromSeconds(second);
            await Task.Delay(800);
            var bytes=await scrub.RenderFrameAsync(1.5,CancellationToken.None);
            var png=Path.Combine(directory,"media-frame.png");File.WriteAllBytes(png,bytes);
            var image=ImageEngine.Load(png);var colors=new HashSet<int>();
            for(var i=0;i+3<image.Bgra.Length&&colors.Count<64;i+=4*37)colors.Add(image.Bgra[i]|image.Bgra[i+1]<<8|image.Bgra[i+2]<<16);
            var frame=scrub.Frame;
            var ok=scrub.FramesRendered>opened&&frame is not null&&frame.PixelSize.Width==320&&frame.PixelSize.Height==240&&colors.Count>=8&&!player.HasAudio&&Math.Abs(player.Duration.TotalSeconds-3)<.3&&failure is null;
            return(ok,$"frames={scrub.FramesRendered} size={frame?.PixelSize} colors={colors.Count} duration={player.Duration.TotalSeconds:0.##} failure={failure}");
        }));

        scenes.Add(("media trim editor",async(window,directory)=>
        {
            var file=await MediaFixture(ffmpeg,directory);var info=await engine.InspectAsync(file);
            var result=ToolEditor.ShowDetailedAsync(window,"trimVideo",[file],media:info,tools:tools);
            for(var n=0;n<100&&ToolEditor.Active is null;n++)await Task.Delay(50);
            var editor=ToolEditor.Active??throw new InvalidOperationException("media editor did not open");
            await Task.Delay(1500);editor.UpdateLayout();
            // Keys: two Right presses put the playhead at 2 s, O sets Out there.
            editor.PressForTest(Key.Right);editor.PressForTest(Key.Right);editor.PressForTest(Key.O);
            var keys=editor.TimelineForTest.Playhead==2&&editor.FieldForTest("End").Text=="2";
            editor.SetRangeForTest(.5,2.0);
            await Task.Delay(500);SaveRender(editor,Path.Combine(directory,"media-editor.png"));
            var timeline=editor.TimelineForTest;var range=timeline.Start==.5&&timeline.End==2.0;
            editor.PressForTest(Key.Enter);
            var accepted=await result;
            if(accepted is null)return(false,"Enter did not accept");
            if(accepted.Media.Start!=.5||accepted.Media.End!=2.0)return(false,$"range {accepted.Media.Start}-{accepted.Media.End}");
            var outputs=await engine.ToolAsync("trimVideo",[file],Path.Combine(directory,"media-trim-out"),accepted.Media);
            var trimmed=await engine.InspectAsync(outputs.Single());
            return(keys&&range&&Math.Abs(trimmed.Duration-1.5)<.25&&trimmed.Video is not null,$"keys={keys} range={range} duration={trimmed.Duration:0.###}");
        }));

        scenes.Add(("media audio extract",async(window,directory)=>
        {
            var file=await MediaFixture(ffmpeg,directory);
            var outputs=await engine.ConvertAsync([file],"mp3",Path.Combine(directory,"media-audio-out"),new MediaOptions());
            var info=await engine.InspectAsync(outputs.Single());
            // Audio-only files preview as a waveform picture.
            using var player=Services.MediaPlayer!();await player.OpenAsync(outputs.Single(),CancellationToken.None);var scrub=(Scrubber)player;
            for(var n=0;n<100&&scrub.FramesRendered==0;n++)await Task.Delay(50);
            return(info.Video is null&&info.Audio is not null&&Math.Abs(info.Duration-3)<.4&&scrub.Frame is {PixelSize.Width:1200},$"video={info.Video is not null} audio={info.Audio is not null} duration={info.Duration:0.###} waveform={scrub.Frame?.PixelSize}");
        }));
    }

    // 3 s test clip: 320x240 at 10 fps with a 440 Hz tone.
    private static async Task<string> MediaFixture(string ffmpeg,string directory)
    {
        var file=Path.Combine(directory,"media-fixture.mp4");if(File.Exists(file))return file;
        await ProcessRunner.RunAsync(ffmpeg,["-v","error","-nostdin","-y","-f","lavfi","-i","testsrc=size=320x240:rate=10","-f","lavfi","-i","sine=frequency=440","-t","3","-pix_fmt","yuv420p",file]);
        return file;
    }
}
