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
    private readonly Toolchain tools=new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Nashi","Tools","v1"),Environment.GetEnvironmentVariable("PATH"));
    private readonly LibraryStore library;
    private readonly PreferencesStore preferences;
    private readonly StackPanel home=new(),workArea=new();
    private readonly DockPanel manage=new();private readonly FileGarden garden=new();
    private readonly ComboBox formatPicker=new(){MinWidth=120};
    private Button? chooseFilesButton,convertButton;private readonly ComboBox appearance;
    private bool busy;
    internal IReadOnlyList<string> SelectedFiles=>files;
    public MainWindow(string[] args)
    {
        launchArgs=args;
        library=new LibraryStore();preferences=new(library.DirectoryPath);L10n.Language=preferences.Value.Language;Nashi.Linux.Theme.Animations=preferences.Value.Animations;Nashi.Linux.Theme.Apply(library.Appearance);Nashi.Linux.Theme.Attach(this);
        Title="Nashi";Width=820;Height=780;MinWidth=700;MinHeight=580;WindowStartupLocation=WindowStartupLocation.CenterScreen;DragDrop.SetAllowDrop(this,true);
        Icon=new WindowIcon(FileGarden.Artwork("NashiIcon"));
        var root=new DockPanel{Margin=new(28,18,28,18)};Content=root;
        var header=new DockPanel{Margin=new(0,0,0,18)};DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var controls=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(controls,Dock.Right);header.Children.Add(controls);
        appearance=new ComboBox{ItemsSource=new[]{"System","Light","Dark"},SelectedItem=char.ToUpper(library.Appearance[0])+library.Appearance[1..],FontSize=11,Margin=new(10,0,0,0)};L10n.Choices(appearance);appearance.SelectionChanged+=(_,_)=>ChangeAppearance();
        var onPc=Text("On your PC",11,"Muted");onPc.VerticalAlignment=VerticalAlignment.Center;controls.Children.Add(onPc);controls.Children.Add(appearance);
        var brand=new StackPanel{Orientation=Orientation.Horizontal};brand.Children.Add(new Image{Source=FileGarden.Artwork("NashiMark"),Width=40,Height=40,Margin=new(0,0,10,0)});brand.Children.Add(Text("Nashi",19));header.Children.Add(brand);
        var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);status.Margin=new(4,14,4,0);bottom.Children.Add(status);bottom.Children.Add(progress);root.Children.Add(bottom);
        var homeScroll=new ScrollViewer{Content=home,VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled};root.Children.Add(homeScroll);
        home.Children.Add(homeTitle);var intro=Text("Convert, fine-tune, and send them on their way.",14,"Muted");intro.Margin=new(0,6,0,22);home.Children.Add(intro);
        home.Children.Add(workArea);
        var drop=new Grid();drop.ColumnDefinitions.Add(new(1,GridUnitType.Star));drop.ColumnDefinitions.Add(new(166,GridUnitType.Pixel));
        var dropText=new StackPanel();drop.Children.Add(dropText);dropText.Children.Add(selectionPill);selection.Margin=new(0,12,0,8);dropText.Children.Add(selection);dropText.Children.Add(fileDescription);
        var picks=new WrapPanel{Margin=new(0,14,0,8)};chooseFilesButton=Primary("Choose files",Choose);picks.Children.Add(chooseFilesButton);dropText.Children.Add(picks);
        Grid.SetColumn(garden,1);drop.Children.Add(garden);
        var dropStack=new StackPanel();dropStack.Children.Add(drop);list.ItemsSource=files;list.ItemTemplate=new FuncDataTemplate<string>((path,_)=>FileRow(path));
        var clear=Button("Clear",()=>{files.Clear();Refresh();});DockPanel.SetDock(clear,Dock.Right);manage.Children.Add(clear);manage.Children.Add(list);dropStack.Children.Add(manage);workArea.Children.Add(Card(dropStack));
        // Temporary stand-in for the conversion wheel: pick an output format and convert the selection.
        L10n.Choices(formatPicker,new ChoiceTextConverter());convertButton=Primary("Convert",async()=>await ConvertSelectedAsync());
        var convert=new DockPanel{Margin=new(0,10,0,0)};DockPanel.SetDock(convertButton,Dock.Right);convertButton.Margin=new(10,0,0,0);convert.Children.Add(convertButton);convert.Children.Add(formatPicker);
        var convertCaption=new StackPanel{Margin=new(0,0,0,10)};convertCaption.Children.Add(Text("Convert",17));convertCaption.Children.Add(Text("Pick a format for the selected files.",12,"Muted"));
        var convertStack=new StackPanel();convertStack.Children.Add(convertCaption);convertStack.Children.Add(convert);actions.Children.Add(Card(convertStack));workArea.Children.Add(actions);
        var footer=new WrapPanel{Margin=new(0,8,0,0)};var mediaBtn=Button("Set up media tools",async()=>await SetupAsync());mediaBtn.Padding=new(12,7,12,7);mediaBtn.FontSize=12;footer.Children.Add(mediaBtn);home.Children.Add(new Expander{Header="Tools & shortcuts",Content=footer,Margin=new(4,8,4,0)});
        Closing+=(_,e)=>{if(busy){e.Cancel=true;L10n.SetText(status,"Finish the current operation or choose Cancel before closing Nashi.");}};
        AddHandler(DragDrop.DragOverEvent,(object? _,DragEventArgs e)=>{e.DragEffects=!busy&&Services.Drop?.Accepts(e)==true?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;});
        AddHandler(DragDrop.DropEvent,async(object? _,DragEventArgs e)=>{e.Handled=true;if(busy||Services.Drop is null)return;await ImportAsync(Services.Drop.ReadAsync(e));});
        AddHandler(KeyDownEvent,(object? _,KeyEventArgs e)=>{if(HandleShortcut(e.Key,e.KeyModifiers))e.Handled=true;},RoutingStrategies.Tunnel);
        if(Services.Theme is {} systemTheme)systemTheme.Changed+=()=>Dispatcher.UIThread.Post(()=>{if(library.Appearance=="system")Nashi.Linux.Theme.Apply("system");});
        Add(args.Where(a=>!a.StartsWith('-')));Refresh();Opened+=(_,_)=>Nashi.Linux.Theme.Entrance(home);
        if(library.RecoveryNotice is {} notice)L10n.SetText(status,notice);
        else if(preferences.RecoveryNotice is {} settingsNotice)L10n.SetText(status,settingsNotice);
        InitLibrary();InitSettings();InitDocuments();InitWeb();InitFileActions();InitDesktop();
    }
    partial void InitLibrary();      // P2-E
    partial void InitSettings();     // P2-F
    partial void InitDocuments();    // P3-B
    partial void InitWeb();          // P3-C
    partial void InitFileActions();  // P3-D
    partial void InitDesktop();      // P3-F
    partial void RenderLibraryRows(); // P2-E: rebuilds Library and recent rows after Record
    private static TextBlock Text(string value,double size,string? color=null,bool localize=true)=>Ui.Text(value,size,color,localize);
    private static Border Card(Control content)=>Ui.Card(content);
    public static Button Button(string title,Action action)=>Ui.Button(title,action);
    internal static Button Primary(string title,Action action)=>Ui.Primary(title,action);
    private Control FileRow(string path)
    {
        var row=new DockPanel();var remove=new Button{Content="✕",Padding=new(8,2,8,2),FontSize=11};L10n.SetTip(remove,"Remove");remove.Click+=(_,_)=>{files.Remove(path);Refresh();};DockPanel.SetDock(remove,Dock.Right);row.Children.Add(remove);
        var name=new TextBlock{Text=Path.GetFileName(path),FontSize=12,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};ToolTip.SetTip(name,path);row.Children.Add(name);return row;
    }
    private void ChangeAppearance()
    {
        if(appearance.SelectedItem is not string choice)return;var value=choice.ToLowerInvariant();
        try{library.SetAppearance(value);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){L10n.SetText(status,"Your files are ready, but Library could not be saved.");}
        Nashi.Linux.Theme.Apply(value);
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
            else if(content.Url is not null)L10n.SetText(status,"Link downloads aren’t available yet.");
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidOperationException){L10n.SetText(status,"Couldn’t read the dropped items: "+error.Message);}
    }
    internal void AddFiles(IEnumerable<string> paths){foreach(var path in paths.Where(p=>File.Exists(p)||Directory.Exists(p))){var full=Path.GetFullPath(path);if(!files.Contains(full))files.Add(full);}Refresh();}
    private void Add(IEnumerable<string> paths)=>AddFiles(paths);
    private void Record(string label,IReadOnlyList<string> paths)
    {
        try{library.Record(label,paths);RenderLibraryRows();}catch(Exception error)when(error is IOException or UnauthorizedAccessException){L10n.SetText(status,"Your files are ready, but Library could not be saved.");}
    }
    internal bool HandleShortcut(Key key,KeyModifiers modifiers)
    {
        if(busy)return false;
        if(modifiers==KeyModifiers.Control&&key==Key.O){Choose();return true;}
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
        if(chooseFilesButton is not null){L10n.Bind(chooseFilesButton,ContentControl.ContentProperty,files.Count==0?"Choose files":"Add or change files");if(files.Count==0){chooseFilesButton.Background=Nashi.Linux.Theme.Primary;chooseFilesButton.Foreground=new SolidColorBrush(Color.FromRgb(43,54,28));}else{Nashi.Linux.Theme.Res(chooseFilesButton,Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty,"Surface");Nashi.Linux.Theme.Res(chooseFilesButton,Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty,"Ink");}}
        manage.IsVisible=files.Count>0;garden.SetSelected(files.Count>0);
        var formats=FeatureCatalog.Formats(files);var previous=formatPicker.SelectedItem as string;formatPicker.ItemsSource=formats;formatPicker.SelectedItem=previous is not null&&formats.Contains(previous)?previous:formats.FirstOrDefault();
        actions.IsVisible=formats.Length>0;
    }
    private void SetBusy(bool value){busy=value;workArea.IsEnabled=!value;appearance.IsEnabled=!value;progress.IsVisible=value;progress.IsIndeterminate=true;}
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
        throw new NotSupportedException("Document conversion isn’t available on Linux yet.");
    }
    private async Task ConvertSelectedAsync()
    {
        if(busy||files.Count==0||formatPicker.SelectedItem is not string format)return;
        if((Video||Audio)&&!tools.Ready){await SetupAsync();if(!tools.Ready)return;}
        SetBusy(true);using var cancellation=new CancellationTokenSource();var toast=new ToastWindow(this,cancellation.Cancel);toast.Show(this);
        var report=new Progress<OperationProgress>(p=>Dispatcher.UIThread.Post(()=>{toast.Update(p);L10n.SetText(status,p.Label);progress.IsIndeterminate=p.Fraction is null;if(p.Fraction is {} fraction)progress.Value=Math.Clamp(fraction,0,1);}));
        try{var outputs=await ConvertAsync(format,report,cancellation.Token);toast.Complete(outputs);Record("Convert to "+format.ToUpperInvariant(),outputs);L10n.SetText(status,$"Saved {outputs.Count} new {(outputs.Count==1?"file":"files")}. Originals kept.");}
        catch(OperationCanceledException){toast.Close();L10n.SetText(status,"Cancelled. Originals kept.");}
        catch(Exception error){toast.Close();await ShowInfoAsync(error.Message,"Couldn’t finish");}
        finally{SetBusy(false);}
    }
    private async Task ShowInfoAsync(string message,string title)
    {
        if(Services.Dialogs is not null)await Services.Dialogs.InfoAsync(this,L10n.Text(message),L10n.Text(title));else L10n.SetText(status,message);
    }
    private async Task SetupAsync()
    {
        if(busy)return;if(tools.Ready){L10n.SetText(status,"Media tools are already installed.");return;}
        if(Services.Dialogs is not null&&!await Services.Dialogs.ConfirmAsync(this,L10n.Text("Download FFmpeg (~180 MB), yt-dlp and Deno directly from their official GitHub releases? SHA-256 hashes are verified before use. FFmpeg is GPL; the other upstream licenses and source links are listed on our Features page. No administrator password is needed."),L10n.Text("Set up media tools")))return;
        SetBusy(true);using var cancellation=new CancellationTokenSource();var toast=new ToastWindow(this,cancellation.Cancel);toast.Show(this);
        var report=new Progress<OperationProgress>(p=>Dispatcher.UIThread.Post(()=>toast.Update(p)));
        try{await Task.Run(()=>tools.InstallAsync(report,cancellation.Token),cancellation.Token);toast.Close();L10n.SetText(status,"Media tools ready. You can edit, convert and download media.");}
        catch(OperationCanceledException){toast.Close();L10n.SetText(status,"Setup cancelled.");}catch(Exception error){toast.Close();await ShowInfoAsync(error.Message,"Setup couldn’t finish");}finally{SetBusy(false);}
    }
}
