using Nashi.Core;
namespace Nashi.Engine;

/// Uses the LibreOffice the distribution provides. Nashi never downloads or installs it on Linux.
public sealed class LinuxOfficeRuntime:IOfficeRuntime
{
    public string? Executable{get
    {
        foreach(var name in new[]{"soffice","libreoffice"})if(FindOnPath(name) is {} found)return found;
        foreach(var path in new[]{"/usr/lib/libreoffice/program/soffice","/usr/lib64/libreoffice/program/soffice"})if(ToolInstallation.UsableFile(path))return path;
        try{if(Directory.Exists("/opt"))foreach(var folder in Directory.GetDirectories("/opt","libreoffice*").Order(StringComparer.Ordinal).Reverse()){var path=Path.Combine(folder,"program","soffice");if(ToolInstallation.UsableFile(path))return path;}}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException){}
        return null;
    }}
    public bool CanInstall=>false;
    public string SetupHint=>"Install LibreOffice with your package manager, e.g. sudo pacman -S libreoffice-fresh / sudo apt install libreoffice";
    public Task InstallAsync(IProgress<OperationProgress>? progress,CancellationToken token)=>Executable is not null?Task.CompletedTask:throw new InvalidOperationException(SetupHint);
    // ProcessRunner needs a fully qualified path, so PATH entries are resolved here.
    internal static string? FindOnPath(string name)
    {
        foreach(var directory in (Environment.GetEnvironmentVariable("PATH")??"").Split(':',StringSplitOptions.RemoveEmptyEntries))
        {
            if(!Path.IsPathFullyQualified(directory))continue;var path=Path.Combine(directory,name);
            if(ToolInstallation.UsableFile(path)&&ToolInstallation.CanExecute(path))return path;
        }
        return null;
    }
}
