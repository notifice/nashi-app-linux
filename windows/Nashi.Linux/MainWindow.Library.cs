using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nashi.Core;
using Nashi.Engine;
using Nashi.Linux.Platform;
namespace Nashi.Linux;

// Port of the Library page from Nashi.Windows/MainWindow.cs and MainWindow.Library.cs. The page and the Home/Library
// header buttons attach through MainWindow's AddPage/ShowPage/AddHeaderButton seams.
internal sealed partial class MainWindow
{
    private readonly StackPanel libraryPage=new(),libraryRows=new(),recentRows=new();
    private readonly TextBox search=new(){MinWidth=200,PlaceholderText="Find a file or tool"};
    private readonly TextBox recentSearch=new(){Width=165,PlaceholderText="Find a recent file"};
    private readonly TextBlock recentCount=Text("0",11,"Muted");
    private readonly Dictionary<Guid,Border> libraryCards=[];
    private ScrollViewer? libraryScroll;
    private Button? homeButton,libraryButton;
    private Guid? selectedLibraryId;
    // Smoke checks render fixed rows here instead of writing into the user's real Library.
    private IReadOnlyList<LibraryEntry>? previewEntries;
    internal bool LibraryVisible=>currentPage=="library";
    internal int LibraryRowCount=>libraryCards.Count;

    partial void InitLibrary()
    {
        homeButton=Button("Home",()=>ShowLibrary(false));libraryButton=Button("Library",()=>ShowLibrary(true));
        homeButton.Padding=libraryButton.Padding=new(10,6);homeButton.FontSize=libraryButton.FontSize=11;
        AddHeaderButton(homeButton);AddHeaderButton(libraryButton);
        L10n.SetTip(homeButton,"Home  ·  Ctrl+1");L10n.SetTip(libraryButton,"Library  ·  Ctrl+2");

        libraryPage.Margin=new(0,0,12,0);libraryPage.Children.Add(Text("Your Library",30));libraryPage.Children.Add(Text("Freshly made, pasted, and ready for another round.",14,"Muted"));
        var searchLabel=Text("Find a file or tool  ·  Ctrl+F",12,"Muted");searchLabel.Margin=new(4,20,4,4);libraryPage.Children.Add(searchLabel);
        L10n.Bind(search,TextBox.PlaceholderTextProperty,"Find a file or tool");search.Margin=new(0,0,0,12);search.HorizontalAlignment=HorizontalAlignment.Stretch;
        libraryPage.Children.Add(search);libraryPage.Children.Add(libraryRows);search.TextChanged+=(_,_)=>RenderLibraryRows();
        libraryScroll=new ScrollViewer{Content=libraryPage,IsVisible=false,VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled};
        AddPage("library",libraryScroll);

        // Recent section on Home, above the "Tools & shortcuts" expander (last home child).
        var recentHeading=new DockPanel{Margin=new(0,24,0,10)};
        var all=Button("See all  ↗",()=>ShowLibrary(true));DockPanel.SetDock(all,Dock.Right);recentHeading.Children.Add(all);
        L10n.Bind(recentSearch,TextBox.PlaceholderTextProperty,"Find a recent file");recentSearch.TextChanged+=(_,_)=>RenderLibraryRows();DockPanel.SetDock(recentSearch,Dock.Right);recentSearch.Margin=new(0,0,8,0);recentHeading.Children.Add(recentSearch);
        var recentTitle=new StackPanel{Orientation=Orientation.Horizontal};recentTitle.Children.Add(Text("Recent",17));recentCount.Margin=new(8,0,0,0);recentTitle.Children.Add(recentCount);recentHeading.Children.Add(recentTitle);
        var at=Math.Max(0,home.Children.Count-1);home.Children.Insert(at,recentRows);home.Children.Insert(at,recentHeading);

        AddHandler(KeyDownEvent,(object? _,KeyEventArgs e)=>{if(LibraryShortcut(e.Key,e.KeyModifiers,FocusManager?.GetFocusedElement()))e.Handled=true;},RoutingStrategies.Tunnel);
        ShowLibrary(false);RenderLibraryRows();
    }

    internal void ShowLibrary(bool visible)
    {
        if(libraryScroll is null)return;
        var changed=LibraryVisible!=visible;
        if(!busy)L10n.SetText(status,"Files stay on your computer.");
        selectedLibraryId??=LibraryEntries().FirstOrDefault()?.Id;
        ShowPage(visible?"library":"home");
        homeButton!.Classes.Set("active",!visible);libraryButton!.Classes.Set("active",visible);
        if(visible)RenderLibraryRows();
        if(changed&&IsLoaded)Nashi.Linux.Theme.Entrance(visible?libraryPage:home);
    }

    /// Smoke hook: shows these rows instead of the stored Library. Pass null to go back to the real one.
    internal void ShowLibraryPreview(IReadOnlyList<LibraryEntry>? entries){previewEntries=entries;selectedLibraryId=null;ShowLibrary(true);RenderLibraryRows();}

    private IReadOnlyList<LibraryEntry> LibraryEntries()=>previewEntries??library.Entries;
    private IEnumerable<LibraryEntry> SearchLibrary(string? query)
    {
        query=(query??"").Trim();
        if(previewEntries is null)return library.Search(query,L10n.Language);
        return previewEntries.Where(e=>query.Length==0||e.Action.Contains(query,StringComparison.CurrentCultureIgnoreCase)||e.Paths.Any(p=>Path.GetFileName(p).Contains(query,StringComparison.CurrentCultureIgnoreCase)));
    }

    partial void RenderLibraryRows()
    {
        if(libraryScroll is null)return;
        libraryCards.Clear();libraryRows.Children.Clear();recentRows.Children.Clear();var filtered=SearchLibrary(search.Text).ToArray();
        if(filtered.Length==0)libraryRows.Children.Add(Card(Text(LibraryEntries().Count==0?"Nothing here yet. Paste something or make your first fresh copy.":"No matches. Try a file name or tool.",14,"Muted")));
        foreach(var entry in filtered)libraryRows.Children.Add(LibraryRow(entry));
        recentCount.Text=LibraryEntries().Count.ToString();foreach(var entry in SearchLibrary(recentSearch.Text).Take(3))recentRows.Children.Add(LibraryRow(entry,true));
        if(LibraryEntries().Count==0)recentRows.Children.Add(Card(Text("Your next fresh copy will land here.",13,"Muted")));
    }

    private Border LibraryRow(LibraryEntry entry,bool compact=false)
    {
        var row=new DockPanel();var available=entry.AvailablePaths;var buttons=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Spacing=4};DockPanel.SetDock(buttons,Dock.Right);row.Children.Add(buttons);
        var reuse=Button("↖",()=>
        {
            if(busy)return;var existing=entry.AvailablePaths;
            if(existing.Length==0){L10n.SetText(status,"These files have moved or were deleted.");RenderLibraryRows();return;}
            files.Clear();Add(existing);ShowLibrary(false);L10n.SetText(status,existing.Length==entry.Paths.Length?"Ready for another round.":"Available files selected. Some files have moved or were deleted.");
        });reuse.IsEnabled=!busy&&available.Length>0;L10n.SetTip(reuse,"Use in Nashi");buttons.Children.Add(reuse);
        var reveal=Button("▱",()=>{var existing=entry.AvailablePaths;if(existing.Length>0)Services.Shell.Reveal(existing[0]);else{L10n.SetText(status,"This file has moved or was deleted.");RenderLibraryRows();}});reveal.IsEnabled=available.Length>0;L10n.SetTip(reveal,"Show in file manager");buttons.Children.Add(reveal);
        var open=Button("◉",()=>{if(entry.AvailablePaths is {Length:>0} existing)Services.Shell.OpenUrl(new Uri(existing[0]).AbsoluteUri);});open.IsEnabled=available.Length>0&&available.All(File.Exists);L10n.SetTip(open,"Open");buttons.Children.Insert(0,open);
        var icon=new Image{Width=29,Height=29,Margin=new(0,0,12,0),VerticalAlignment=VerticalAlignment.Center};LoadThumbnail(icon,entry.Paths[0]);DockPanel.SetDock(icon,Dock.Left);row.Children.Add(icon);
        var text=new StackPanel{Margin=new(0,0,12,0),VerticalAlignment=VerticalAlignment.Center};var name=Text(entry.Name,12,localize:false);name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;ToolTip.SetTip(name,string.Join(Environment.NewLine,entry.Paths));text.Children.Add(name);
        var info=new StackPanel{Orientation=Orientation.Horizontal};info.Children.Add(Text(entry.Action,10,"Muted"));info.Children.Add(Text("  ·  "+entry.Created.ToLocalTime().ToString("g"),10,"Muted",localize:false));
        if(available.Length<entry.Paths.Length){info.Children.Add(Text("  ·  ",10,"Muted",localize:false));info.Children.Add(Text("Files missing",10,"Muted"));}
        text.Children.Add(info);row.Children.Add(text);
        var card=Card(row);if(!compact)ConfigureLibraryRow(card,entry);card.Padding=new(14,10,14,10);
        if(compact){card.CornerRadius=new(12);card.BorderThickness=new(0);card.Margin=new(0,0,0,2);}
        return card;
    }

    // Thumbnails load off the UI thread; non-images keep a neutral placeholder glyph.
    private static async void LoadThumbnail(Image target,string path)
    {
        if(Services.Artwork is null)return;
        try
        {
            var pixels=await Services.Artwork.ThumbnailAsync(path,58,CancellationToken.None);
            if(pixels is not null)target.Source=ToBitmap(pixels);
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException){}
    }
    internal static WriteableBitmap ToBitmap(PixelImage image)
    {
        var bitmap=new WriteableBitmap(new PixelSize(image.Width,image.Height),new Vector(96,96),PixelFormat.Bgra8888,AlphaFormat.Unpremul);
        using var frame=bitmap.Lock();
        for(var y=0;y<image.Height;y++)Marshal.Copy(image.Bgra,y*image.Width*4,frame.Address+y*frame.RowBytes,image.Width*4);
        return bitmap;
    }

    private void ConfigureLibraryRow(Border card,LibraryEntry entry)
    {
        PointerPressedEventArgs? pressed=null;var origin=new Point();
        libraryCards[entry.Id]=card;card.Tag=entry;card.Focusable=true;L10n.SetTip(card,"Enter: use in Nashi");
        card.BorderBrush=selectedLibraryId==entry.Id?Nashi.Linux.Theme.Brush("Gold"):Nashi.Linux.Theme.Brush("Line");
        card.GotFocus+=(_,_)=>SelectCard(entry.Id);
        card.PointerPressed+=(_,e)=>
        {
            SelectCard(entry.Id);
            if(e.Source is Visual source&&source.FindAncestorOfType<Avalonia.Controls.Button>(true) is null){card.Focus();if(e.ClickCount==2)OpenEntry(entry);pressed=e;origin=e.GetPosition(card);}else pressed=null;
        };
        // Dragging a row out hands its files to other apps; Avalonia needs the original press event to start the drag.
        card.PointerReleased+=(_,_)=>pressed=null;
        card.PointerMoved+=async(_,e)=>
        {
            if(pressed is null||Services.Drag is null||!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed)return;
            var delta=e.GetPosition(card)-origin;if(Math.Abs(delta.X)<6&&Math.Abs(delta.Y)<6)return;
            var trigger=pressed;pressed=null;var existing=entry.AvailablePaths;if(existing.Length==0)return;
            try{await Services.Drag.StartFilesAsync(this,trigger,existing);}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidOperationException){L10n.SetText(status,"Couldn’t start the drag: "+error.Message);}
        };
    }
    // Space preview: one window per request, closed with Space or Escape.
    internal FilePreviewWindow PreviewFiles(IEnumerable<string> paths){var preview=new FilePreviewWindow(paths,tools);preview.Show(this);return preview;}
    private void OpenEntry(LibraryEntry entry){if(entry.AvailablePaths is {Length:>0} existing&&existing.All(File.Exists))Services.Shell.OpenUrl(new Uri(existing[0]).AbsoluteUri);else L10n.SetText(status,"These files have moved or were deleted.");}
    private void SelectCard(Guid id){selectedLibraryId=id;foreach(var pair in libraryCards)pair.Value.BorderBrush=Nashi.Linux.Theme.Brush(pair.Key==id?"Gold":"Line");}
    private void SelectLibrary(int direction)
    {
        var entries=SearchLibrary(search.Text).ToArray();if(entries.Length==0)return;
        var old=Array.FindIndex(entries,e=>e.Id==selectedLibraryId);var next=old<0?0:Math.Clamp(old+direction,0,entries.Length-1);
        SelectCard(entries[next].Id);if(libraryCards.TryGetValue(entries[next].Id,out var card)){card.Focus();card.BringIntoView();}
    }
    private LibraryEntry? SelectedLibrary()=>SearchLibrary(search.Text).FirstOrDefault(e=>e.Id==selectedLibraryId)??SearchLibrary(search.Text).FirstOrDefault();
    private void ReuseSelectedLibrary(){if(busy||SelectedLibrary() is not {} entry)return;files.Clear();Add(entry.AvailablePaths);ShowLibrary(false);}

    private bool LibraryShortcut(Key key,KeyModifiers modifiers,object? focus)
    {
        if(modifiers==KeyModifiers.Control&&key==Key.D1){ShowLibrary(false);return true;}
        if(modifiers==KeyModifiers.Control&&key==Key.D2){ShowLibrary(true);return true;}
        if(modifiers==KeyModifiers.Control&&key==Key.F){ShowLibrary(true);search.Focus();search.SelectAll();return true;}
        if(busy)return false;
        if(LibraryVisible&&key==Key.Down&&modifiers==KeyModifiers.None&&ReferenceEquals(focus,search)){SelectLibrary(1);return true;}
        if(ClipboardImport.IsEditing(focus))return false;
        if(modifiers==KeyModifiers.None&&key==Key.Space&&focus is not Avalonia.Controls.Button)
        {
            if(LibraryVisible&&SelectedLibrary() is {} previewed&&previewed.AvailablePaths is {Length:>0} available){PreviewFiles(available);return true;}
            if(currentPage=="home"&&list.IsKeyboardFocusWithin&&files.Count>0){PreviewFiles(list.SelectedItems?.Cast<string>().ToArray() is {Length:>0} chosen?chosen:files.ToArray());return true;}
        }
        if(LibraryVisible&&modifiers==KeyModifiers.None&&key is Key.Up or Key.Down){SelectLibrary(key==Key.Down?1:-1);return true;}
        if(LibraryVisible&&modifiers==KeyModifiers.None&&key==Key.Enter&&focus is not Avalonia.Controls.Button){ReuseSelectedLibrary();return true;}
        if(modifiers==KeyModifiers.Control&&key==Key.V){_=ImportClipboardAsync();return true;}
        return false;
    }

    /// Ctrl+V: files, images and text become the file selection; links are reported until downloads land.
    internal async Task ImportClipboardAsync(bool record=true)
    {
        if(busy)return;
        try
        {
            var result=await ClipboardImport.ReadAsync(this,library);if(result.Link is null)ShowLibrary(false);
            if(result.Link is not null){OpenWebLink(result.Link);return;}
            files.Clear();AddFiles(result.Paths);
            if(record)Record(result.Label,result.Paths);
            L10n.SetText(status,result.Label+". Choose a tool or create a ZIP.");
        }
        catch(Exception error)when(ClipboardImport.IsClipboardError(error)){L10n.SetText(status,"Couldn’t paste: "+error.Message);}
    }
}
