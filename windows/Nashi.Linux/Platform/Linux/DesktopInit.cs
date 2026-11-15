namespace Nashi.Linux.Platform;

internal static partial class Services
{
    static partial void InitDesktop()=>Tray=new Linux.TrayService();
}
