using Nashi.Core;
namespace Nashi.Linux.Platform;

// One place for the media tool folder, matching the Windows app (LocalApplicationData/Nashi/Tools/v1, then PATH).
internal static class ToolPaths
{
    internal static string MediaDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Nashi","Tools","v1");
    internal static Toolchain CreateToolchain()=>new(MediaDirectory,Environment.GetEnvironmentVariable("PATH"));
}
