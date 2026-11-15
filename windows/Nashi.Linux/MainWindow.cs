using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Nashi.Core;
using Nashi.Engine;
using Nashi.Linux.Platform;
namespace Nashi.Linux;

// Port of Nashi.Windows/MainWindow.cs. Root file owned by P1-B; other tasks add features
// through these partial hooks in their own MainWindow.<Area>.cs files and never edit this file.
internal sealed partial class MainWindow:Window
{
    private readonly string[] launchArgs;
    private readonly ObservableCollection<string> files=[];
    private readonly ListBox list=new(){MinHeight=0,MaxHeight=140,SelectionMode=SelectionMode.Multiple};
    private readonly StackPanel actions=new();
    private readonly TextBlock selectionPill=Text("✦  START HERE",11,"Gold");
    private readonly TextBlock homeTitle=Text("Your files. Their next form.",30);
    private readonly TextBlock selection=new(){FontSize=23,FontWeight=FontWeight.Bold,TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock status=Text("Files stay on your computer.",12,"Muted");
    private readonly ProgressBar progress=new(){Height=4,Minimum=0,Maximum=1,IsVisible=false,Margin=new(4,8,4,0)};
    private readonly TextBlock fileDescription=Text("Images, videos, documents, and all the little things in between.",12,"Muted");
    private readonly Toolchain tools=Nashi.Linux.Platform.ToolPaths.CreateToolchain();
    private readonly LibraryStore library;
    private readonly PreferencesStore preferences;
    private readonly StackPanel home=new(),workArea=new();
    private readonly DockPanel manage=new();private readonly FileGarden garden=new();
    private Button? chooseFilesButton,openWheelButton;private readonly StackPanel shortcutHints=new(){Orientation=Orientation.Horizontal,Margin=new(0,12,0,0)};
    // Page seams: Library, Settings and later pages attach through AddPage/ShowPage/AddHeaderButton instead of walking the layout tree.
    private readonly WrapPanel headerControls=new(){HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};
    private readonly Grid pageHost=new();
    private readonly Dictionary<string,Control> pages=[];
    private ScrollViewer homePage=null!;
    private string currentPage="home";
    private int headerButtonCount;
    private bool busy;
    internal IReadOnlyList<string> SelectedFiles=>files;
    internal string StatusForTest=>status.Text??"";
    internal void ClearFilesForTest(){files.Clear();Refresh();}
    public MainWindow(string[] args)
    {
        launchArgs=args;
        library=new LibraryStore();preferences=new(library.DirectoryPath);L10n.Language=preferences.Value.Language;Nashi.Linux.Theme.Animations=preferences.Value.Animations;Nashi.Linux.Theme.Apply(library.Appearance);Nashi.Linux.Theme.Attach(this);
        Title="Nashi";Width=820;Height=780;MinWidth=700;MinHeight=580;WindowStartupLocation=WindowStartupLocation.CenterScreen;DragDrop.SetAllowDrop(this,true);
        Icon=new WindowIcon(FileGarden.Artwork("NashiIcon"));
        var root=new DockPanel{Margin=new(28,18,28,18)};home.Margin=new(0,0,12,0);Content=root;
        var header=new DockPanel{Margin=new(0,0,0,18)};DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var controls=headerControls;DockPanel.SetDock(controls,Dock.Right);header.Children.Add(controls);
        var onPc=Text("On your PC",11,"Muted");onPc.VerticalAlignment=VerticalAlignment.Center;controls.Children.Add(onPc);
        var brand=new StackPanel{Orientation=Orientation.Horizontal};brand.Children.Add(new Image{Source=FileGarden.Artwork("NashiMark"),Width=40,Height=40,Margin=new(0,0,10,0)});brand.Children.Add(Text("Nashi",19));header.Children.Add(brand);
        var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);status.Margin=new(4,14,4,0);bottom.Children.Add(status);bottom.Children.Add(progress);root.Children.Add(bottom);
        homePage=new ScrollViewer{Content=home,VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled};root.Children.Add(pageHost);AddPage("home",homePage);ShowPage("home");
        home.Children.Add(homeTitle);var intro=Text("Convert, fine-tune, and send them on their way.",14,"Muted");intro.Margin=new(0,6,0,22);home.Children.Add(intro);
        home.Children.Add(workArea);
        var drop=new Grid();drop.ColumnDefinitions.Add(new(1,GridUnitType.Star));drop.ColumnDefinitions.Add(new(166,GridUnitType.Pixel));
        var dropText=new StackPanel();drop.Children.Add(dropText);dropText.Children.Add(selectionPill);selection.Margin=new(0,12,0,8);dropText.Children.Add(selection);dropText.Children.Add(fileDescription);
        var picks=new WrapPanel{Margin=new(0,14,0,8)};chooseFilesButton=Primary("Choose files",Choose);picks.Children.Add(chooseFilesButton);picks.Children.Add(Button("Paste",()=>_=ImportClipboardAsync()));dropText.Children.Add(picks);
        Grid.SetColumn(garden,1);drop.Children.Add(garden);
        var dropStack=new StackPanel();dropStack.Children.Add(drop);list.ItemsSource=files;list.ItemTemplate=new FuncDataTemplate<string>((path,_)=>FileRow(path));
        var clear=Button("Clear",()=>{files.Clear();Refresh();});DockPanel.SetDock(clear,Dock.Right);manage.Children.Add(clear);manage.Children.Add(list);dropStack.Children.Add(manage);BuildShortcutHints();dropStack.Children.Add(shortcutHints);workArea.Children.Add(Card(dropStack));
        // The wheel opens from this button, Ctrl+Space or Enter; it replaces the old format picker.
        openWheelButton=Primary("Open wheel  ·  Ctrl+Space",()=>ShowWheel());L10n.SetTip(openWheelButton,"Tools, conversions and file actions");openWheelButton.HorizontalAlignment=HorizontalAlignment.Left;openWheelButton.Margin=new(0,10,0,0);
        var wheelCaption=new StackPanel();wheelCaption.Children.Add(Text("Tools & conversions",17));wheelCaption.Children.Add(Text("Convert, resize, crop and more for the selected files.",12,"Muted"));wheelCaption.Children.Add(openWheelButton);actions.Children.Add(Card(wheelCaption));workArea.Children.Add(actions);
        var footer=new WrapPanel{Margin=new(0,8,0,0)};var mediaBtn=Button("Set up media tools",async()=>await SetupAsync());mediaBtn.Padding=new(12,7,12,7);mediaBtn.FontSize=12;footer.Children.Add(mediaBtn);home.Children.Add(new Expander{Header="Tools & shortcuts",Content=footer,Margin=new(4,8,4,0)});
        Closing+=(_,e)=>{if(busy){e.Cancel=true;L10n.SetText(status,"Finish the current operation or choose Cancel before closing Nashi.");}};
        AddHandler(DragDrop.DragOverEvent,(object? _,DragEventArgs e)=>{e.DragEffects=!busy&&Services.Drop?.Accepts(e)==true?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;});
        AddHandler(DragDrop.DropEvent,async(object? _,DragEventArgs e)=>{e.Handled=true;if(busy||Services.Drop is null)return;await ImportAsync(Services.Drop.ReadAsync(e));});
        AddHandler(KeyDownEvent,(object? _,KeyEventArgs e)=>{if(HandleShortcut(e.Key,e.KeyModifiers))e.Handled=true;},RoutingStrategies.Tunnel);
        if(Services.Theme is {} systemTheme)systemTheme.Changed+=()=>Dispatcher.UIThread.Post(()=>{if(library.Appearance=="system")Nashi.Linux.Theme.Apply("system");});
        Add(args.Where(a=>!a.StartsWith('-')));Refresh();Opened+=(_,_)=>Nashi.Linux.Theme.Entrance(home);
        if(library.RecoveryNotice is {} notice)L10n.SetText(status,notice);
        else if(preferences.RecoveryNotice is {} settingsNotice)L10n.SetText(status,settingsNotice);
        InitLibrary();InitSettings();InitTools();InitDocuments();InitWeb();InitFileActions();InitDesktop();InitUpdates();
    }
    partial void InitLibrary();      // P2-E
    partial void InitSettings();     // P2-F
    partial void InitTools();        // wheel and tool flow (MainWindow.Tools.cs)
    partial void InitDocuments();    // P3-B
    partial void InitWeb();          // P3-C
    partial void InitFileActions();  // P3-D
    partial void InitDesktop();      // P3-F
    partial void InitUpdates();      // P4-D (MainWindow.Updates.cs)
    partial void RenderLibraryRows(); // P2-E: rebuilds Library and recent rows after Record
    private static TextBlock Text(string value,double size,string? color=null,bool localize=true)=>Ui.Text(value,size,color,localize);
    private static Border Card(Control content)=>Ui.Card(content);
    public static Button Button(string title,Action action)=>Ui.Button(title,action);
    internal static Button Primary(string title,Action action)=>Ui.Primary(title,action);
    // Windows lists the drag chords; only the shortcuts that are true on Linux are shown.
    private void BuildShortcutHints()
    {
        foreach(var hint in new[]{"Ctrl+Space  Wheel","Ctrl+O  Choose","Ctrl+V  Paste"}){var pill=Card(Text(hint,11,"Muted"));pill.Padding=new(9,5,9,5);pill.Margin=new(0,0,7,0);pill.CornerRadius=new(9);shortcutHints.Children.Add(pill);}
    }
    private Control FileRow(string path)
    {
        var row=new DockPanel();var remove=new Button{Content="✕",Padding=new(8,2,8,2),FontSize=11};L10n.SetTip(remove,"Remove");remove.Click+=(_,_)=>{files.Remove(path);Refresh();};DockPanel.SetDock(remove,Dock.Right);row.Children.Add(remove);
        var name=new TextBlock{Text=Path.GetFileName(path),FontSize=12,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};ToolTip.SetTip(name,path);row.Children.Add(name);return row;
    }
    private async void Choose()
    {
        if(busy||Services.Files is null)return;
        var picked=await Services.Files.OpenFilesAsync(this,L10n.Text("Choose files for Nashi"));if(picked.Count>0)AddFiles(picked);
    }
    private async Task ImportAsync(Task<TransferContent> read)
    {
        try
        {
            var content=await read;
            if(content.Files.Count>0)AddFiles(content.Files);
            else if((content.Url??WebMediaPayload.Parse(content.Text,content.Html)?.Url) is {} link)OpenWebLink(link);
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidOperationException){L10n.SetText(status,"Couldn’t read the dropped items: "+error.Message);}
    }
    internal void AddFiles(IEnumerable<string> paths){foreach(var path in paths.Where(p=>File.Exists(p)||Directory.Exists(p))){var full=Path.GetFullPath(path);if(!files.Contains(full))files.Add(full);}Refresh();}
    private void Add(IEnumerable<string> paths)=>AddFiles(paths);
    /// Smoke hook: when set, Record appends here instead of writing into the user's real Library.
    internal List<(string Label,string[] Paths)>? RecordedForTest{get;set;}
    private void Record(string label,IReadOnlyList<string> paths)
    {
        if(RecordedForTest is not null){RecordedForTest.Add((label,paths.ToArray()));return;}
        try{library.Record(label,paths);RenderLibraryRows();}catch(Exception error)when(error is IOException or UnauthorizedAccessException){L10n.SetText(status,"Your files are ready, but Library could not be saved.");}
    }
    internal bool HandleShortcut(Key key,KeyModifiers modifiers)
    {
        if(busy)return false;
        if(modifiers==KeyModifiers.Control&&key==Key.O){Choose();return true;}
        if(modifiers==KeyModifiers.Control&&key==Key.Space&&currentPage=="home"){ShowWheel();return true;}
        if(modifiers==KeyModifiers.None&&key==Key.Delete&&list.IsKeyboardFocusWithin)
        {
            var selected=list.SelectedItems?.Cast<string>().ToArray()??[];if(selected.Length==0)return false;
            foreach(var path in selected)files.Remove(path);Refresh();return true;
        }
        return false;
    }
    private bool All(string[] extensions)=>files.Count>0&&files.All(p=>extensions.Contains(Path.GetExtension(p).TrimStart('.').ToLowerInvariant()));
    private bool Images=>files.Count>0&&files.All(FeatureCatalog.IsImage);
    private bool Video=>All(["mp4","mov","mkv","webm","avi","wmv","m4v","gif"]);
    private bool Audio=>All(["mp3","m4a","wav","flac","ogg","opus","aiff","aif","wma","aac"]);
    private void Refresh()
    {
        // UI copy goes through L10n so it follows the language setting; file names and folder paths stay verbatim.
        L10n.SetText(selectionPill,files.Count==0?"✦  START HERE":"✦  READY WHEN YOU ARE");
        L10n.SetText(homeTitle,files.Count==0?"Your files. Their next form.":"Let’s make something of it.");
        if(files.Count==1){selection.Text=Path.GetFileName(files[0]);fileDescription.Text=Path.GetDirectoryName(files[0])!;}
        else{L10n.SetText(selection,files.Count==0?"Drop something good.":$"{files.Count} files, ready to go.");L10n.SetText(fileDescription,files.Count==0?"Images, videos, documents, and all the little things in between.":"Keep them together, or give each one a fresh start.");}
        if(chooseFilesButton is not null){L10n.Bind(chooseFilesButton,ContentControl.ContentProperty,files.Count==0?"Choose files":"Add or change files");chooseFilesButton.Classes.Set("primary",files.Count==0);}
        manage.IsVisible=files.Count>0;garden.SetSelected(files.Count>0);
        actions.IsVisible=files.Count>0;
    }
    private void SetBusy(bool value){busy=value;workArea.IsEnabled=!value;progress.IsVisible=value;progress.IsIndeterminate=true;UpdateToolsSetupLabel();}
    /// Adds a page to the host beside Home. Pages start hidden; ShowPage switches between them.
    internal void AddPage(string key,Control page){pages[key]=page;page.IsVisible=key==currentPage;if(!pageHost.Children.Contains(page))pageHost.Children.Add(page);}
    internal void ShowPage(string key)
    {
        if(!pages.ContainsKey(key))throw new ArgumentException("Unknown page "+key);
        currentPage=key;foreach(var (name,page) in pages)page.IsVisible=name==key;
    }
    internal string CurrentPage=>currentPage;
    /// Adds a button to the header, before the "On your PC" label, in call order.
    internal void AddHeaderButton(Control button){headerControls.Children.Insert(Math.Min(headerButtonCount,headerControls.Children.Count),button);headerButtonCount++;}
    private string OutputDirectory(IReadOnlyList<string> inputs)=>preferences.Value.OutputMode=="folder"&&!string.IsNullOrWhiteSpace(preferences.Value.OutputFolder)?preferences.Value.OutputFolder:Path.GetDirectoryName(inputs[0])!;
    /// Converts the current selection to <paramref name="format"/> off the UI thread and returns the written paths. Used by the Convert button and the smoke test.
    internal async Task<IReadOnlyList<string>> ConvertAsync(string format,IProgress<OperationProgress>? report=null,CancellationToken token=default)
    {
        var inputs=files.ToArray();if(inputs.Length==0)throw new InvalidOperationException("Choose files first.");
        var destination=OutputDirectory(inputs);var profile=preferences.Profile(format);
        if(Images&&ImageEngine.Formats.Contains(format))
            return await Task.Run<IReadOnlyList<string>>(()=>
            {
                using var tx=new ExportTransaction(destination);
                var exported=ImageEngine.Apply("convert",inputs,tx.StagingDirectory,new ImageOptions(Quality:(int)Math.Round(profile.Quality*100))with{EncoderPath=tools.Ready?tools.Executable("ffmpeg"):null},format,token,report);
                return tx.Publish(exported,token);
            },token);
        if(Video||Audio)
        {
            if(!tools.Ready)throw new FileNotFoundException("Choose Set up media tools once to enable media editing.");
            var engine=new MediaEngine(tools.Executable("ffmpeg"),tools.Executable("ffprobe"));var options=new MediaOptions{Quality=profile.Quality,AudioBitrate=profile.AudioBitrate,SampleDepth=profile.SampleDepth,GifFrameRate=profile.GifFrameRate,GifWidth=profile.GifWidth,PreserveMetadata=profile.PreserveMetadata,Height=profile.MaxVideoHeight>0?profile.MaxVideoHeight:null};
            return await Task.Run(()=>engine.ConvertAsync(inputs,format,destination,options,report,token),token);
        }
        return await ConvertDocumentsAsync(inputs,format,destination,new DocumentOptions(profile.Dpi,profile.Layout,profile.EpubLayout),report,token);
    }
    /// Runs <paramref name="work"/> with the busy state, progress toast, Library record and status line shared by every tool.
    private async Task RunWithToastAsync(string label,Func<IProgress<OperationProgress>,CancellationToken,Task<IReadOnlyList<string>>> work)
    {
        if(busy)return;
        SetBusy(true);using var cancellation=new CancellationTokenSource();var toast=OpenToast(cancellation.Cancel);
        // Progress callbacks arrive asynchronously; once the run ended they must not overwrite the final status.
        var running=true;
        var report=new Progress<OperationProgress>(p=>Dispatcher.UIThread.Post(()=>{if(!running)return;toast.Update(p);L10n.SetText(status,p.Label);progress.IsIndeterminate=p.Fraction is null;if(p.Fraction is {} fraction)progress.Value=Math.Clamp(fraction,0,1);}));
        try
        {
            var outputs=await work(report,cancellation.Token);running=false;toast.Complete(outputs);Record(label,outputs);L10n.SetText(status,$"Saved {outputs.Count} new {(outputs.Count==1?"file":"files")}. Originals kept.");
            if(preferences.Value.RevealFinished&&outputs.Count>0)Services.Shell?.Reveal(outputs[0]);
        }
        catch(OperationCanceledException){running=false;toast.Close();L10n.SetText(status,"Cancelled. Originals kept.");}
        catch(Exception error){running=false;toast.Close();L10n.SetText(status,"Couldn’t finish. Originals kept.");await ShowInfoAsync(error.Message,"Couldn’t finish");}
        finally{SetBusy(false);}
    }
    private async Task ConvertWithToastAsync(string format)
    {
        if(busy||files.Count==0)return;
        if((Video||Audio)&&!tools.Ready){await SetupAsync();if(!tools.Ready)return;}
        await RunWithToastAsync("Convert to "+format.ToUpperInvariant(),(report,token)=>ConvertAsync(format,report,token));
    }
    private async Task ShowInfoAsync(string message,string title)
    {
        if(Services.Dialogs is not null)await Services.Dialogs.InfoAsync(DialogOwner,L10n.Text(message),L10n.Text(title));else L10n.SetText(status,message);
    }
    private async Task SetupAsync()
    {
        if(busy)return;if(tools.Ready){L10n.SetText(status,"Media tools are already installed.");return;}
        if(Services.Dialogs is not null&&!await Services.Dialogs.ConfirmAsync(DialogOwner,L10n.Text("Download FFmpeg (~180 MB), yt-dlp and Deno directly from their official GitHub releases? SHA-256 hashes are verified before use. FFmpeg is GPL; the other upstream licenses and source links are listed on our Features page. No administrator password is needed."),L10n.Text("Set up media tools")))return;
        SetBusy(true);using var cancellation=new CancellationTokenSource();var toast=OpenToast(cancellation.Cancel);
        var report=new Progress<OperationProgress>(p=>Dispatcher.UIThread.Post(()=>toast.Update(p)));
        try{await Task.Run(()=>tools.InstallAsync(report,cancellation.Token),cancellation.Token);toast.Close();L10n.SetText(status,"Media tools ready. You can edit, convert and download media.");}
        catch(OperationCanceledException){toast.Close();L10n.SetText(status,"Setup cancelled.");}catch(Exception error){toast.Close();await ShowInfoAsync(error.Message,"Setup couldn’t finish");}finally{SetBusy(false);}
    }
}
