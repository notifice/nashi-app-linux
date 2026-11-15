using Avalonia.Controls;
using Nashi.Core;
namespace Nashi.Linux;

// Port of Nashi.Windows/DocumentEditorOptions.cs. documentLayout and epubLayout live in EditorProfiles.cs.
internal sealed record SplitChoice(int PagesPerFile,string PageRanges);
internal sealed partial class ToolEditor
{
    internal string PageRanges=>fields.GetValueOrDefault("Page ranges")?.Text??"";
    internal int PagesPerFile=>(int)Value("Pages per file",1);
    private readonly TextBox archivePassword=new(){PasswordChar='•'};
    partial void BuildDocumentOptions()
    {
        if(tool=="splitPDF"){Field("Pages per file",1);TextField("Page ranges","");}
        if(FeatureCatalog.Extension(input)=="pdf"){options.Children.Add(L10n.Label(new TextBlock{Text="PDF password (if needed)",Margin=new(4,12,4,0)}));options.Children.Add(archivePassword);}
        if(tool is "convert"&&media is null&&bitmap is null)
        {Field("DPI",144);options.Children.Add(L10n.Label(new TextBlock{Text="PDF → Office layout"}));options.Children.Add(documentLayout);options.Children.Add(L10n.Label(new TextBlock{Text="EPUB layout"}));options.Children.Add(epubLayout);}
        if(tool=="extractArchive"||FeatureCatalog.IsArchive(input)){options.Children.Add(L10n.Label(new TextBlock{Text="Archive password (if needed)",Margin=new(4,12,4,0)}));options.Children.Add(archivePassword);}
    }
    partial void ReadDocumentOptions(ref DocumentOptions documentOptions)
    {
        documentOptions=documentOptions with{Dpi=(int)Value("DPI",documentOptions.Dpi),Password=archivePassword.Text??""};
    }
}
