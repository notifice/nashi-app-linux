namespace Nashi.Linux.Platform;

internal static partial class Services
{
    static partial void InitDrag()=>Drag=new Linux.DragSource();
}
