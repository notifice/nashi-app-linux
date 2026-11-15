using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Nashi.Core;
using Nashi.Linux.Platform;
namespace Nashi.Linux;

// Port of the download card/page from Nashi.Windows/MainWindow.cs and WebMediaOptions.cs. The page attaches through AddPage/ShowPage.
internal sealed partial class MainWindow
{
    private readonly TextBox webLink=new();
    private readonly ComboBox downloadKind=new(){ItemsSource=new[]{"video","audio","thumbnail"},SelectedIndex=0,MinWidth=110};
    private readonly ComboBox webQuality=new(){ItemsSource=WebMediaOptions.QualityLabels,SelectedIndex=0};
    private readonly TextBlock webSummary=new(){Text="Paste a public media link to begin.",TextWrapping=Avalonia.Media.TextWrapping.Wrap,Margin=new(0,6,0,12)};
    private readonly StackPanel photoChoices=new(),webContent=new();
    private readonly HashSet<int> selectedWebPhotos=[];
    private CancellationTokenSource? inspection;
    private string? inspectedLink,webPayloadKind;
    private StackPanel? webQualityPanel;
    private TextBlock? webLocation;
    internal string WebLinkText=>webLink.Text??"";
    internal IReadOnlyList<string> WebKinds=>(downloadKind.ItemsSource as IEnumerable<string>)?.ToArray()??[];
    internal string? WebKind=>downloadKind.SelectedItem as string;
    internal bool WebVisible=>currentPage=="web";

    partial void InitWeb()
    {
        L10n.Bind(webLink,TextBox.PlaceholderTextProperty,"Paste a public video, image or audio page link");L10n.SetTip(webLink,"Paste a public video, image or audio page link");
        Avalonia.Automation.AutomationProperties.SetAutomationId(webLink,"DownloadLink");
        // Home card.
        var arrow=Text("↓",28,"Gold");arrow.Margin=new(0,0,14,0);DockPanel.SetDock(arrow,Dock.Left);
        var shortcut=Text("Ctrl+Shift+D",12,"Muted");DockPanel.SetDock(shortcut,Dock.Right);
        var caption=new StackPanel();caption.Children.Add(Text("Download a link",15));caption.Children.Add(Text("Video, audio, or an image. Straight into your tools.",12,"Muted"));
        var cardRow=new DockPanel();cardRow.Children.Add(shortcut);cardRow.Children.Add(arrow);cardRow.Children.Add(caption);
        var cardButton=new Button{Content=cardRow,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new(22,16,22,16),Margin=new(0,0,0,6)};cardButton.Classes.Add("card");cardButton.Click+=(_,_)=>OpenDownload();
        workArea.Children.Insert(workArea.Children.IndexOf(actions),cardButton);

        // Download page.
        var back=Button("← Home",()=>ShowLibrary(false));back.HorizontalAlignment=HorizontalAlignment.Left;back.FontSize=11;back.Margin=new(0,0,0,14);
        webContent.Margin=new(0,0,12,0);webContent.Children.Add(back);
        webContent.Children.Add(Text("Download from the web",27));webContent.Children.Add(Text("A link. A file. Yours to work with.",14,"Muted"));
        var card=new StackPanel();card.Children.Add(Text("Download a link",17));card.Children.Add(Text("Video, audio, or an image. Straight into your tools.",12,"Muted"));
        var save=Primary("Download ↗",async()=>await DownloadAsync());DockPanel.SetDock(save,Dock.Right);DockPanel.SetDock(downloadKind,Dock.Right);downloadKind.Margin=new(8,0,8,0);
        var entry=new DockPanel{Margin=new(0,12,0,0)};entry.Children.Add(save);entry.Children.Add(downloadKind);entry.Children.Add(webLink);card.Children.Add(entry);
        var cardHint=Text("Ctrl+Shift+D  Use a copied link  ·  Public, downloadable media",11,"Muted");cardHint.Margin=new(4,6,0,0);card.Children.Add(cardHint);
        var webCard=Card(card);webCard.Margin=new(0,20,0,0);webContent.Children.Add(webCard);BuildWebOptions();
        var scroll=new ScrollViewer{Content=webContent,IsVisible=false,VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled};
        AddPage("web",scroll);

        AddHandler(KeyDownEvent,(object? _,KeyEventArgs e)=>
        {
            if(busy)return;
            if(e.KeyModifiers==(KeyModifiers.Control|KeyModifiers.Shift)&&e.Key==Key.D){_=OpenCopiedLinkAsync();e.Handled=true;}
            else if(e.KeyModifiers==KeyModifiers.None&&e.Key==Key.Escape&&WebVisible){ShowLibrary(false);e.Handled=true;}
        },RoutingStrategies.Tunnel);
    }

    private void BuildWebOptions()
    {
        var buttons=new WrapPanel{Margin=new(-4,10,0,0)};buttons.Children.Add(Button("Find media",async()=>await InspectWebLinkAsync()));
        buttons.Children.Add(Button("Paste link",async()=>{try{if(Services.Clipboard is not null){var read=await Services.Clipboard.ReadAsync(this);webLink.Text=read.Url??read.Text?.Trim()??"";}}catch(Exception e)when(ClipboardImport.IsClipboardError(e)){L10n.SetText(webSummary,"Clipboard is busy. Try Paste again.");}}));
        webContent.Children.Add(buttons);webContent.Children.Add(webSummary);
        var quality=new StackPanel();quality.Children.Add(Text("Video quality",12,"Muted"));L10n.Choices(webQuality);webQuality.Margin=new(0,4,4,4);quality.Children.Add(webQuality);webQualityPanel=quality;webContent.Children.Add(quality);
        downloadKind.SelectionChanged+=(_,_)=>quality.IsVisible=downloadKind.SelectedItem as string=="video";webContent.Children.Add(photoChoices);
        var destination=new DockPanel{Margin=new(0,6,0,0)};webLocation=Text(preferences.Value.DownloadFolder,12,"Muted",false);
        var change=Button("Change…",async()=>
        {
            if(Services.Files is null)return;var folder=await Services.Files.OpenFolderAsync(this,L10n.Text("Save downloaded media to…"),preferences.Value.DownloadFolder);
            if(folder is null)return;preferences.Save(preferences.Value with{DownloadFolder=folder});webLocation.Text=folder;
        });
        DockPanel.SetDock(change,Dock.Right);destination.Children.Add(change);destination.Children.Add(webLocation);var destinationCard=Card(destination);destinationCard.Padding=new(20,8,12,8);webContent.Children.Add(destinationCard);
        var note=Text("Instagram photo posts open a photo selection. Reel links offer video and audio. Public downloadable media only.",11,"Muted");note.Margin=new(4,8,4,16);webContent.Children.Add(note);
        webLink.TextChanged+=(_,_)=>
        {
            inspection?.Cancel();inspectedLink=null;photoChoices.Children.Clear();selectedWebPhotos.Clear();
            var payload=WebMediaPayload.Parse(webLink.Text);if(payload is not null&&payload.Kind!=webPayloadKind){webPayloadKind=payload.Kind;SetDownloadKind(payload);}
            L10n.SetText(webSummary,"Choose Find media to inspect this link.");
        };
    }

    private void SetDownloadKind(WebMediaPayload payload,string? chosen=null)
    {
        var formats=WebMediaOptions.Kinds(payload);downloadKind.ItemsSource=formats;downloadKind.SelectedItem=formats.Contains(chosen??"")?chosen:formats[0];
        if(webQualityPanel is not null)webQualityPanel.IsVisible=downloadKind.SelectedItem as string=="video";
    }

    internal void OpenDownload(){if(webLocation is not null)webLocation.Text=preferences.Value.DownloadFolder;ShowPage("web");webLink.Focus();}

    /// Shows the download page with <paramref name="url"/> ready. Used by paste, drop and the copied-link shortcut.
    internal void OpenWebLink(string url,string? chosen=null)
    {
        if(busy)return;
        var payload=WebMediaPayload.Parse(url);webLink.Text=payload?.Url??url;if(payload is not null){webPayloadKind=payload.Kind;SetDownloadKind(payload,chosen);}
        OpenDownload();L10n.SetText(status,"Link ready. Choose a format, then Download.");
    }

    internal async Task OpenCopiedLinkAsync()
    {
        if(busy||Services.Clipboard is null)return;
        try
        {
            var read=await Services.Clipboard.ReadAsync(this);var payload=WebMediaPayload.Parse(read.Text,read.Html,read.Url);
            if(payload is null){L10n.SetText(status,"Copy a public media link first.");return;}
            OpenWebLink(payload.Url);
        }
        catch(Exception error)when(ClipboardImport.IsClipboardError(error)){L10n.SetText(status,"Clipboard is busy. Try again.");}
    }

    /// Fills the summary and format choices from inspected media; the smoke test feeds a fake info here without any network.
    internal void ApplyWebInfo(WebMediaInfo info)
    {
        L10n.SetText(webSummary,WebMediaOptions.Summary(info));
        if(info.AudioOnly){downloadKind.ItemsSource=new[]{"audio"};downloadKind.SelectedIndex=0;}
    }

    private async Task InspectWebLinkAsync()
    {
        if(busy)return;inspection?.Cancel();var pending=new CancellationTokenSource();inspection=pending;var input=WebLinkText;var payload=WebMediaPayload.Parse(input);
        if(payload is null){L10n.SetText(webSummary,"Paste one complete public media link.");return;}
        SetDownloadKind(payload,downloadKind.SelectedItem as string);photoChoices.Children.Clear();selectedWebPhotos.Clear();L10n.SetText(webSummary,"Finding the media…");
        try
        {
            var engine=new WebDownload(tools);
            if(payload.Kind=="instagramPhotos")
            {
                if(!tools.Ready)throw new InvalidOperationException("Set up media tools once to inspect Instagram posts.");
                var photos=await engine.InspectPhotosAsync(payload.Url,pending.Token);if(input!=WebLinkText||pending.IsCancellationRequested)return;
                L10n.SetText(webSummary,$"{photos.Length} photos · full-frame images · saved in post order");
                var row=new WrapPanel();row.Children.Add(Button("All",()=>{foreach(var box in photoChoices.Children.OfType<CheckBox>())box.IsChecked=true;}));row.Children.Add(Button("None",()=>{foreach(var box in photoChoices.Children.OfType<CheckBox>())box.IsChecked=false;}));photoChoices.Children.Add(row);
                foreach(var photo in photos)
                {
                    selectedWebPhotos.Add(photo.Id);var box=new CheckBox{Content=$"Photo {photo.Id}  ·  {photo.Width} × {photo.Height}",IsChecked=true,Margin=new(4,9,4,9)};
                    box.IsCheckedChanged+=(_,_)=>{if(box.IsChecked==true)selectedWebPhotos.Add(photo.Id);else selectedWebPhotos.Remove(photo.Id);};photoChoices.Children.Add(box);
                }
            }
            else if(payload.Kind=="image"||downloadKind.SelectedItem as string=="thumbnail")L10n.SetText(webSummary,payload.Kind=="image"?"Original image · ready to download":"YouTube thumbnail · best available full-size image");
            else
            {
                if(!tools.Ready)throw new InvalidOperationException("Set up media tools once to inspect this media.");
                var info=await engine.InspectAsync(payload.Url,pending.Token);if(input!=WebLinkText||pending.IsCancellationRequested)return;ApplyWebInfo(info);
            }
            inspectedLink=input;
        }
        catch(OperationCanceledException){}
        catch(Exception error){if(!pending.IsCancellationRequested)L10n.SetText(webSummary,error.Message);}
        finally{if(inspection==pending)inspection=null;pending.Dispose();}
    }

    private async Task DownloadAsync()
    {
        if(busy)return;
        var kind=downloadKind.SelectedItem as string??"video";var payload=WebMediaPayload.Parse(WebLinkText);string url;
        try{url=WebMediaOptions.Url(WebLinkText);}catch(ArgumentException error){await ShowInfoAsync(error.Message,"Check your link");return;}
        if(kind=="photos"&&inspectedLink!=WebLinkText){await InspectWebLinkAsync();return;}
        if(WebMediaOptions.NeedsTools(kind,payload)&&!tools.Ready){await SetupAsync();if(!tools.Ready)return;}
        var destination=preferences.Value.DownloadFolder;int? height=kind=="video"?WebMediaOptions.MaxHeight(webQuality.SelectedIndex):null;var photos=kind=="photos"?new HashSet<int>(selectedWebPhotos):null;
        try{Directory.CreateDirectory(destination);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){await ShowInfoAsync(error.Message,"Check your link");return;}
        await RunWithToastAsync("Download "+kind,(report,token)=>new WebDownload(tools).DownloadAsync(url,kind,destination,report,token,height,photos));
    }
}
