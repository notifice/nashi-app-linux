using System.Diagnostics;
namespace Nashi.Linux.Platform.Linux;

internal sealed class ShellService:IShellService
{
    public void OpenUrl(string url)=>Spawn("xdg-open",[url]);

    public void Reveal(string path)
    {
        var full=Path.GetFullPath(path);
        var uri=new Uri(full).AbsoluteUri.Replace("'","%27");
        var ok=Run("gdbus",["call","--session","--dest","org.freedesktop.FileManager1","--object-path","/org/freedesktop/FileManager1","--method","org.freedesktop.FileManager1.ShowItems","['"+uri+"']",""],2500)==0;
        if(!ok)Spawn("xdg-open",[Directory.Exists(full)?full:Path.GetDirectoryName(full)??full]);
    }

    public void OpenTerminal(string folder)
    {
        var candidates=new List<string>();
        var env=Environment.GetEnvironmentVariable("TERMINAL");
        if(!string.IsNullOrWhiteSpace(env))candidates.Add(env);
        candidates.AddRange(["xdg-terminal-exec","konsole","gnome-terminal","kitty","alacritty","foot","xterm"]);
        foreach(var c in candidates)
        {
            var exe=Which(c);
            if(exe is not null&&Spawn(exe,[],folder))return;
        }
    }

    public bool SameDirectory(string a,string b)
    {
        var ia=Identity(a);var ib=Identity(b);
        if(ia is not null&&ib is not null)return ia==ib;
        return string.Equals(Resolve(a),Resolve(b),StringComparison.Ordinal);
    }

    // device:inode via `stat -L`, so symlinks and bind mounts of the same directory compare equal.
    private static string? Identity(string path)
    {
        try
        {
            var psi=new ProcessStartInfo("stat"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
            foreach(var x in new[]{"-L","-c","%d:%i","--",path})psi.ArgumentList.Add(x);
            using var p=Process.Start(psi);
            if(p is null)return null;
            var o=p.StandardOutput.ReadToEnd().Trim();
            if(!p.WaitForExit(2000)){try{p.Kill();}catch{}return null;}
            return p.ExitCode==0&&o.Length>0?o:null;
        }
        catch{return null;}
    }

    private static string Resolve(string path)
    {
        var full=Path.GetFullPath(path).TrimEnd('/');
        try{return (Directory.ResolveLinkTarget(full,true)?.FullName??full).TrimEnd('/');}
        catch{return full;}
    }

    private static string? Which(string name)
    {
        if(name.Contains('/'))return File.Exists(name)?name:null;
        foreach(var dir in (Environment.GetEnvironmentVariable("PATH")??"").Split(':',StringSplitOptions.RemoveEmptyEntries))
        {
            var p=Path.Combine(dir,name);
            if(File.Exists(p))return p;
        }
        return null;
    }

    private static bool Spawn(string exe,IEnumerable<string> args,string? cwd=null)
    {
        try
        {
            var psi=new ProcessStartInfo(exe){UseShellExecute=false};
            foreach(var a in args)psi.ArgumentList.Add(a);
            if(cwd is not null&&Directory.Exists(cwd))psi.WorkingDirectory=cwd;
            using var p=Process.Start(psi);
            return p is not null;
        }
        catch{return false;}
    }

    private static int Run(string exe,IEnumerable<string> args,int timeoutMs)
    {
        try
        {
            var psi=new ProcessStartInfo(exe){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var a in args)psi.ArgumentList.Add(a);
            using var p=Process.Start(psi);
            if(p is null)return -1;
            // Drain output asynchronously so the timeout still applies when the child hangs.
            _=p.StandardOutput.ReadToEndAsync();_=p.StandardError.ReadToEndAsync();
            if(!p.WaitForExit(timeoutMs)){try{p.Kill();}catch{}return -1;}
            return p.ExitCode;
        }
        catch{return -1;}
    }
}
