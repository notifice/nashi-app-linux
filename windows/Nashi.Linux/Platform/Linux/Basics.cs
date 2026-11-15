namespace Nashi.Linux.Platform;

internal static partial class Services
{
    static partial void InitBasics()
    {
        Files=new Linux.FilePicker();Dialogs=new Linux.Dialogs();Shell=new Linux.ShellService();
        Screen=new Linux.ScreenService();Theme=new Linux.SystemTheme();Drop=new Linux.DropReader();
    }
}
