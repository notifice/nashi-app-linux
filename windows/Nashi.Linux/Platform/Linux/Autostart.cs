using System.Text;
namespace Nashi.Linux.Platform.Linux;

// XDG autostart: $XDG_CONFIG_HOME (or ~/.config)/autostart/nashi.desktop starts Nashi hidden at login.
internal sealed class Autostart:IAutostart
{
    // The spec ignores a relative XDG_CONFIG_HOME. Read on every call so a changed environment takes effect.
    private static string FilePath
    {
        get
        {
            var config=Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if(string.IsNullOrEmpty(config)||!Path.IsPathRooted(config))config=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".config");
            return Path.Combine(config,"autostart","nashi.desktop");
        }
    }
    public bool Enabled
    {
        get
        {
            try
            {
                if(!File.Exists(FilePath))return false;
                var lines=File.ReadAllLines(FilePath).Select(l=>l.Trim().Replace(" ",""));
                return !lines.Any(l=>l.Equals("Hidden=true",StringComparison.OrdinalIgnoreCase)||l.Equals("X-GNOME-Autostart-enabled=false",StringComparison.OrdinalIgnoreCase));
            }
            catch(Exception error)when(error is IOException or UnauthorizedAccessException){return false;}
        }
    }
    public void Set(bool enabled)
    {
        var path=FilePath;
        if(!enabled){if(File.Exists(path))File.Delete(path);return;}
        var process=Environment.ProcessPath??throw new InvalidOperationException("Nashi could not find its own program path.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temporary,Entry(process),new UTF8Encoding(false));File.Move(temporary,path,true);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    internal static string Entry(string process)=>"[Desktop Entry]\nType=Application\nName=Nashi\nComment=Convert and edit files locally\nExec="+Quote(process)+" --background\nTerminal=false\nX-GNOME-Autostart-enabled=true\n";
    // Desktop Entry spec: an Exec argument with special characters is double-quoted; inside quotes " ` $ \ take a backslash,
    // and the string-value layer then doubles every backslash. A literal % becomes %%.
    internal static string Quote(string argument)
    {
        var text=new StringBuilder("\"");
        foreach(var c in argument)
            text.Append(c switch{'"'=>"\\\\\"",'`'=>"\\\\`",'$'=>"\\\\$",'\\'=>"\\\\\\\\",'%'=>"%%",'\n'=>" ",_=>c.ToString()});
        return text.Append('"').ToString();
    }
}
