namespace Nashi.Linux.Platform;

internal static partial class Services
{
    static partial void InitClipboard()
    {
        Clipboard=new Linux.ClipboardService();
        Artwork=new Linux.FileArtwork();
    }
}
