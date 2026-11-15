using Avalonia.Controls;
using Avalonia.Input;
using Nashi.Core;
using Nashi.Engine;
namespace Nashi.Linux.Platform;

// Content read from clipboard or a drop. Files are absolute local paths.
public sealed record TransferContent(IReadOnlyList<string> Files,byte[]? ImagePng,string? Text,string? Html,string? Url)
{ public static readonly TransferContent Empty=new([],null,null,null,null); }

public interface IClipboardService{ Task<TransferContent> ReadAsync(Window owner); Task SetTextAsync(Window owner,string text); Task SetFilesAsync(Window owner,IReadOnlyList<string> paths); }
public interface IDropReader{ bool Accepts(DragEventArgs e); Task<TransferContent> ReadAsync(DragEventArgs e); }
public interface IDragSource{ Task<bool> StartFilesAsync(Window owner,PointerEventArgs trigger,IReadOnlyList<string> paths); }
public interface IFilePicker
{
    Task<IReadOnlyList<string>> OpenFilesAsync(Window owner,string title,bool multiple=true,IReadOnlyList<string>? extensions=null);
    Task<string?> OpenFolderAsync(Window owner,string title,string? start=null);
}
public interface IDialogs
{
    Task InfoAsync(Window? owner,string message,string title);
    Task<bool> ConfirmAsync(Window? owner,string message,string title);
}
public interface IShellService{ void OpenUrl(string url); void Reveal(string path); void OpenTerminal(string folder); bool SameDirectory(string a,string b); }
public interface IScreenService{ void FitToWorkArea(Window window); bool ReducedMotion{get;} }
public interface ISystemTheme{ bool Dark{get;} event Action? Changed; }
public interface IAutostart{ bool Enabled{get;} void Set(bool enabled); }
public interface ITrayService:IDisposable{ bool Available{get;} void Install(Action open,Func<IReadOnlyList<(string Title,Action Run)>> menu); void UpdateAvailable(string version); }
public interface IFileArtwork{ Task<PixelImage?> ThumbnailAsync(string path,int size,CancellationToken token); }
public interface ISoundPlayer{ bool Enabled{get;set;} void Play(string asset); void Stop(); }
public interface IMediaPlayer:IDisposable
{
    bool HasAudio{get;} Control View{get;}
    Task OpenAsync(string path,CancellationToken token);
    void Play(); void Pause(); bool IsPlaying{get;}
    TimeSpan Position{get;set;} TimeSpan Duration{get;}
    event Action? PositionChanged; event Action? Ended; event Action<string>? Failed;
}
public sealed record AvailableUpdate(string Version,string Url);
public interface IUpdateInstaller
{
    Version Current{get;} bool SelfUpdateSupported{get;}
    Task<AvailableUpdate?> CheckAsync(CancellationToken token);
    Task StageAsync(AvailableUpdate update,IProgress<OperationProgress> progress,CancellationToken token);
}
