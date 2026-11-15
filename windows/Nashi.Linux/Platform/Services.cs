using Nashi.Engine;
namespace Nashi.Linux.Platform;

// Static locator, assigned once in Program.Main, matching the code-behind style of Nashi.Windows.
// A service stays null until its task lands; callers must null-check optional ones (Tray, Media, Sound, Artwork, Updates).
internal static partial class Services
{
    internal static IFilePicker Files{get;set;}=null!;
    internal static IDialogs Dialogs{get;set;}=null!;
    internal static IShellService Shell{get;set;}=null!;
    internal static IScreenService Screen{get;set;}=null!;
    internal static ISystemTheme Theme{get;set;}=null!;
    internal static IDropReader Drop{get;set;}=null!;
    internal static IClipboardService? Clipboard{get;set;}
    internal static IDragSource? Drag{get;set;}
    internal static IAutostart? Autostart{get;set;}
    internal static ITrayService? Tray{get;set;}
    internal static IFileArtwork? Artwork{get;set;}
    internal static ISoundPlayer? Sound{get;set;}
    internal static Func<IMediaPlayer>? MediaPlayer{get;set;}
    internal static IUpdateInstaller? Updates{get;set;}
    internal static IPdfRenderer? Pdf{get;set;}
    internal static ITextRecognizer? Ocr{get;set;}
    internal static IOfficeRuntime? Office{get;set;}
    // Each Platform/Linux task implements one hook in its own file.
    static partial void InitBasics();     // P1-C: Files, Dialogs, Shell, Screen, Theme, Drop
    static partial void InitClipboard();  // P2-E: Clipboard, Artwork
    static partial void InitSettings();   // P2-F: Autostart
    static partial void InitSound();      // P2-D: Sound
    static partial void InitEngine();     // P2-B/P2-C: Pdf, Ocr, Office
    static partial void InitMedia();      // P3-A: MediaPlayer
    static partial void InitDrag();       // P3-D: Drag
    static partial void InitDesktop();    // P3-F: Tray
    static partial void InitUpdates();    // P4-D: Updates
    internal static void Initialize(){InitBasics();InitClipboard();InitSettings();InitSound();InitEngine();InitMedia();InitDrag();InitDesktop();InitUpdates();}
}
