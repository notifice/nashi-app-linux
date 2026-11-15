namespace Nashi.Linux.Platform;

internal static partial class Services
{
    static partial void InitSound()=>Sound=new Linux.SoundPlayer();
}
