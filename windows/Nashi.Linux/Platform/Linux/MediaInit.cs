using Nashi.Core;
namespace Nashi.Linux.Platform;

internal static partial class Services
{
    // Same tool folder and PATH lookup as MainWindow's Toolchain. Without ffmpeg and ffprobe there is no player and callers fall back to their still-frame or "not available" paths.
    static partial void InitMedia()
    {
        var tools=Nashi.Linux.Platform.ToolPaths.CreateToolchain();
        try
        {
            var ffmpeg=tools.Executable("ffmpeg");var ffprobe=tools.Executable("ffprobe");
            MediaPlayer=()=>new Linux.FrameScrubPlayer(ffmpeg,ffprobe);
        }
        catch(FileNotFoundException){}
    }
}
