using Avalonia.Controls;
namespace Nashi.Linux;

// Port of Nashi.Windows/MainWindow.cs. Root file owned by P1-B; other tasks add features
// through these partial hooks in their own MainWindow.<Area>.cs files and never edit this file.
internal sealed partial class MainWindow:Window
{
    private readonly string[] launchArgs;
    public MainWindow(string[] args)
    {
        launchArgs=args;Title="Nashi";Width=1100;Height=760;MinWidth=760;MinHeight=560;
        this[!BackgroundProperty]=new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Paper");
        Content=new TextBlock{Text="Nashi",Margin=new(24)};
        InitLibrary();InitSettings();InitDocuments();InitWeb();InitFileActions();InitDesktop();
    }
    partial void InitLibrary();      // P2-E
    partial void InitSettings();     // P2-F
    partial void InitDocuments();    // P3-B
    partial void InitWeb();          // P3-C
    partial void InitFileActions();  // P3-D
    partial void InitDesktop();      // P3-F
}
