using System.Diagnostics;
using Avalonia.Platform;
namespace Nashi.Linux.Platform.Linux;

// Plays a bundled WAV through the first of pw-play, paplay or aplay found on PATH; silent when none exists or a launch fails.
internal sealed class SoundPlayer:ISoundPlayer
{
    private const double Volume=.65;
    private readonly object gate=new();
    private readonly List<Process> running=[];
    private readonly Dictionary<string,string> cached=[];
    private (string Program,string[] Arguments)? player;private bool searched;
    public bool Enabled{get;set;}=true;
    public void Play(string asset)
    {
        if(!Enabled)return;
        try
        {
            lock(gate)
            {
                var (program,arguments)=Resolve();if(program is null)return;
                var file=Extract(asset);var info=new ProcessStartInfo(program){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                foreach(var argument in arguments)info.ArgumentList.Add(argument);info.ArgumentList.Add(file);
                var process=Process.Start(info);if(process is null)return;
                running.Add(process);process.EnableRaisingEvents=true;
                process.OutputDataReceived+=(_,_)=>{};process.ErrorDataReceived+=(_,_)=>{};process.BeginOutputReadLine();process.BeginErrorReadLine();
                process.Exited+=(_,_)=>{lock(gate)running.Remove(process);process.Dispose();};
            }
        }
        catch(Exception error)when(error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException){}
    }
    public void Stop()
    {
        lock(gate)
        {
            foreach(var process in running.ToArray())try{if(!process.HasExited)process.Kill();}catch(InvalidOperationException){}
            running.Clear();
        }
    }
    private (string? Program,string[] Arguments) Resolve()
    {
        if(!searched)
        {
            searched=true;
            foreach(var (name,arguments) in new (string,string[])[]{("pw-play",[$"--volume={Volume:0.00}"]),("paplay",[$"--volume={(int)(Volume*65536)}"]),("aplay",["-q"])})
                if(OnPath(name) is {} found){player=(found,arguments);break;}
        }
        return player is {} p?(p.Program,p.Arguments):(null,[]);
    }
    private static string? OnPath(string name)=>(Environment.GetEnvironmentVariable("PATH")??"").Split(':',StringSplitOptions.RemoveEmptyEntries).Select(d=>Path.Combine(d,name)).FirstOrDefault(File.Exists);
    // The asset is copied out of the assembly once; later calls reuse the cache file.
    private string Extract(string asset)
    {
        if(cached.TryGetValue(asset,out var existing)&&File.Exists(existing))return existing;
        var root=Environment.GetEnvironmentVariable("XDG_CACHE_HOME");if(string.IsNullOrEmpty(root))root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".cache");
        var directory=Path.Combine(root,"nashi");Directory.CreateDirectory(directory);var file=Path.Combine(directory,Path.GetFileName(new Uri(asset).AbsolutePath));
        if(!File.Exists(file)){var temporary=file+"."+Environment.ProcessId+".tmp";using(var source=AssetLoader.Open(new Uri(asset)))using(var target=File.Create(temporary))source.CopyTo(target);File.Move(temporary,file,true);}
        cached[asset]=file;return file;
    }
}
