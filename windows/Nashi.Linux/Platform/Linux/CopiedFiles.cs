using System.Diagnostics;
namespace Nashi.Linux.Platform.Linux;

// Files copied in a file manager, read with wl-paste: under XWayland the app cannot see the Wayland clipboard while unfocused, so the launching process reads it.
internal static class CopiedFiles
{
    private const int Max=256;
    private static readonly TimeSpan Timeout=TimeSpan.FromSeconds(2);
    internal static IReadOnlyList<string> Read()
    {
        try
        {
            if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))||!OnPath("wl-paste"))return[];
            var types=Run("--no-newline","--list-types");if(types is null)return[];
            var names=types.Split('\n',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
            var gnome=false;string type;
            if(names.Contains("text/uri-list"))type="text/uri-list";
            else if(names.Contains("x-special/gnome-copied-files")){type="x-special/gnome-copied-files";gnome=true;}
            else return[];
            var text=Run("--no-newline","--type",type);
            return text is null?[]:Parse(text,gnome);
        }
        catch(Exception){return[];}
    }
    /// Parses a text/uri-list (or x-special/gnome-copied-files, whose first line is copy or cut) into existing local paths.
    internal static IReadOnlyList<string> Parse(string text,bool gnome)
    {
        var result=new List<string>();
        try
        {
            var lines=text.Split('\n',StringSplitOptions.TrimEntries);
            foreach(var line in gnome?lines.Skip(1):lines)
            {
                if(line.Length==0||line[0]=='#')continue;
                if(!Uri.TryCreate(line,UriKind.Absolute,out var uri)||!uri.IsFile)continue;
                var path=uri.LocalPath;
                if((File.Exists(path)||Directory.Exists(path))&&!result.Contains(path)){result.Add(path);if(result.Count>=Max)break;}
            }
        }
        catch(Exception){}
        return result;
    }
    private static bool OnPath(string name)=>(Environment.GetEnvironmentVariable("PATH")??"").Split(':',StringSplitOptions.RemoveEmptyEntries).Any(d=>File.Exists(Path.Combine(d,name)));
    private static string? Run(params string[] arguments)
    {
        var info=new ProcessStartInfo("wl-paste"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
        foreach(var a in arguments)info.ArgumentList.Add(a);
        using var process=Process.Start(info);if(process is null)return null;
        var output=process.StandardOutput.ReadToEndAsync();process.StandardError.ReadToEndAsync();
        if(!process.WaitForExit(Timeout)){try{process.Kill(true);}catch(Exception){}return null;}
        return process.ExitCode==0&&output.Wait(Timeout)?output.Result:null;
    }
}
