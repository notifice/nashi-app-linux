namespace Nashi.Linux.Platform;

internal static partial class Services
{
    static partial void InitUpdates()=>Updates=new Linux.ReleaseNotifier();
}
