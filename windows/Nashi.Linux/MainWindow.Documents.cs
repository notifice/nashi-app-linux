using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Nashi.Core;
using Nashi.Engine;
using Nashi.Linux.Platform;
namespace Nashi.Linux;

// Port of Nashi.Windows/MainWindow.Documents.cs: document conversions, PDF tools, QR reading and metadata editing.
// OCR runs inside DocumentConversion (scanned PDF to text/Office). Protect and unlock exist on Windows only as the
// "PDF password" field of the editors, which DocumentOptions.Password carries through.
internal sealed partial class MainWindow
{
    /// Converts documents (and archives) with the platform PDF renderer, OCR and Office runtime. Without LibreOffice the Office routes fail with its setup hint.
    internal async Task<IReadOnlyList<string>> ConvertDocumentsAsync(IReadOnlyList<string> inputs,string format,string destination,DocumentOptions options,IProgress<OperationProgress>? report,CancellationToken token)
    {
        var office=Services.Office??throw new InvalidOperationException("Office conversion isn’t available.");
        var conversion=new DocumentConversion(office,tools,Services.Pdf??throw new InvalidOperationException("PDF rendering isn’t available."),Services.Ocr??throw new InvalidOperationException("Text recognition isn’t available."));
        try{return await Task.Run(()=>conversion.ConvertAsync(inputs,format,destination,options,report,token),token);}
        catch(InvalidOperationException error)when(office.Executable is null&&error.Message.Contains("office tools",StringComparison.OrdinalIgnoreCase)){throw new InvalidOperationException(office.SetupHint,error);}
    }
    /// Sends a wheel choice to the document flow when it belongs there. Returns false for choices another flow owns.
    private async Task<bool> RunDocumentRouteAsync(ToolChoice choice)
    {
        var selected=files.ToArray();
        if(choice.Id=="removeMetadata"){await EditMetadataAsync(selected);return true;}
        if(choice.Id=="readQRCode"){await ReadQrAsync(selected);return true;}
        if(Images||Video||Audio||choice.Id=="extractArchive")return false;
        if(choice.Id.StartsWith("convert:",StringComparison.Ordinal))
        {
            var format=choice.Id[8..];var title="Convert to "+format.ToUpperInvariant();
            var accepted=await ToolEditor.ShowDetailedAsync(this,"convert",selected,preferences,format,FeatureCatalog.Formats(selected),title);if(accepted is null)return true;
            await RunWithToastAsync(title,(report,token)=>ConvertDocumentsAsync(selected,accepted.Format,OutputDirectory(selected),accepted.Document,report,token));return true;
        }
        if(choice.Id=="organizePDF")
        {
            var organizer=await PdfOrganizer.CreateAsync(selected[0]);if(!await organizer.ShowDialog<bool>(this))return true;
            // Read the organizer on the UI thread; the export runs on a worker thread.
            var pages=organizer.Pages;var rotations=organizer.Rotations;var pageRotations=organizer.PageRotations;var uniformWidth=organizer.UniformWidth;var destination=OutputDirectory(selected);
            await RunWithToastAsync("Organize PDF",(report,token)=>Task.Run(()=>DocumentEngine.Pdf("organizePDF",selected,destination,pages,token,rotations,pageRotations:pageRotations,uniformWidth:uniformWidth),token));return true;
        }
        if(choice.Id is not ("mergePDFs" or "splitPDF" or "compress"))return false;
        var result=await ToolEditor.ShowDetailedAsync(this,choice.Id,selected,title:choice.Title);if(result is null)return true;
        var password=result.Document.Password;var ordered=result.OrderedInputs;var outDirectory=OutputDirectory(selected);
        if(choice.Id=="compress"){await RunWithToastAsync("Compress PDF",(report,token)=>PdfCompression.RunAsync(selected,outDirectory,result.Image,password,report,token,Services.Pdf!));return true;}
        var split=result.Split;
        await RunWithToastAsync(choice.Title,(report,token)=>Task.Run<IReadOnlyList<string>>(()=>DocumentEngine.Pdf(choice.Id,ordered,outDirectory,null,token,pagesPerFile:split.PagesPerFile,password:password,pageRanges:split.PageRanges),token));
        return true;
    }
    private async Task EditMetadataAsync(string[] selected)
    {
        if(selected.All(p=>FeatureCatalog.IsAudio(p)||FeatureCatalog.IsVideo(p)&&FeatureCatalog.Extension(p)!="gif")){await EditMediaMetadataAsync(selected);return;}
        if(metadataService.Executable is null)
        {
            if(Services.Dialogs is not null&&!await Services.Dialogs.ConfirmAsync(this,L10n.Text("Download ExifTool (~11 MB) from its official SourceForge distribution? The SHA-256 is checked and it runs locally without an administrator password. Upstream licenses are retained."),L10n.Text("Set up metadata tools")))return;
            SetBusy(true);using var cancellation=new CancellationTokenSource();var toast=new ToastWindow(this,cancellation.Cancel);toast.Show(this);
            var report=new Progress<OperationProgress>(p=>Dispatcher.UIThread.Post(()=>toast.Update(p)));
            try{await metadataService.InstallAsync(report,cancellation.Token);}
            catch(OperationCanceledException){toast.Close();L10n.SetText(status,"Setup cancelled.");return;}
            catch(Exception error){toast.Close();await ShowInfoAsync(error.Message,"Setup couldn’t finish");return;}
            finally{toast.Close();SetBusy(false);}
        }
        var removeAll=selected.Length>1;Dictionary<string,string>? changes=null;
        if(selected.Length==1)
        {
            IReadOnlyDictionary<string,string> values;
            try{values=await metadataService.ReadAsync(selected[0]);}catch(Exception error){await ShowInfoAsync(error.Message,"Couldn’t read metadata");return;}
            var editor=new MetadataEditor(values);if(!await editor.ShowDialog<bool>(this))return;removeAll=editor.RemoveAll;changes=removeAll?null:editor.Updates;
        }
        await RunWithToastAsync("Metadata",(report,token)=>metadataService.WriteAsync(selected,OutputDirectory(selected),removeAll,changes,progress:report,token:token));
    }
    private async Task ReadQrAsync(IReadOnlyList<string> inputs)
    {
        SetBusy(true);var results=new List<(string File,string Value)>();
        try
        {
            foreach(var file in inputs)
            {
                if(FeatureCatalog.Extension(file)=="pdf")
                {
                    var pages=await (Services.Pdf??throw new InvalidOperationException("PDF rendering isn’t available.")).RenderAsync(file,180);
                    for(var n=0;n<pages.Length;n++){var page=ImageEngine.FromPng(pages[n]);foreach(var value in await Task.Run(()=>QrDecoder.Decode(page)))results.Add((Path.GetFileName(file)+$" · Page {n+1}",value));}
                }
                else{var image=await Task.Run(()=>ImageEngine.Load(file));foreach(var value in await Task.Run(()=>QrDecoder.Decode(image)))results.Add((Path.GetFileName(file),value));}
            }
        }
        catch(Exception error)when(error is not OperationCanceledException){await ShowInfoAsync(error.Message,"Couldn’t read QR codes");return;}
        finally{SetBusy(false);}
        var window=new Window{Title="Read QR Codes · Nashi",Width=720,Height=520,WindowStartupLocation=WindowStartupLocation.CenterOwner};Nashi.Linux.Theme.Attach(window);
        var stack=new StackPanel{Margin=new(26)};stack.Children.Add(Text("Read QR Codes",28));stack.Children.Add(Text(results.Count==0?"No QR codes found. Try a larger or sharper image.":$"{results.Count} QR {(results.Count==1?"code":"codes")} found.",14,"Muted"));
        foreach(var result in results)
        {
            var content=new StackPanel();content.Children.Add(Text(result.File,12,"Muted",false));content.Children.Add(new TextBox{Text=result.Value,IsReadOnly=true,TextWrapping=Avalonia.Media.TextWrapping.Wrap,MaxHeight=150});
            var buttons=new WrapPanel();buttons.Children.Add(Button("Copy",async()=>{if(Services.Clipboard is not null)try{await Services.Clipboard.SetTextAsync(window,result.Value);}catch(Exception error)when(ClipboardImport.IsClipboardError(error)){await ShowInfoAsync("Clipboard is busy. Try again.","Nashi");}}));
            if(Uri.TryCreate(result.Value,UriKind.Absolute,out var url)&&url.Scheme is "http" or "https")buttons.Children.Add(Button("Open link",()=>Services.Shell.OpenUrl(url.AbsoluteUri)));
            content.Children.Add(buttons);stack.Children.Add(Card(content));
        }
        window.Content=new ScrollViewer{Content=stack};await window.ShowDialog(this);
    }
}
