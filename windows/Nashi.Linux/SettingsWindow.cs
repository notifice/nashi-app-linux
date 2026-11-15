using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Nashi.Core;
using Nashi.Linux.Platform;
namespace Nashi.Linux;

// Port of Nashi.Windows/SettingsWindow.cs. Hidden on Linux: drag shortcuts, window management, drag modifiers,
// float behavior (global drag, window manager hooks) and Explorer reveal. Launch at login goes through Services.Autostart.
internal sealed partial class SettingsWindow:Window
{
    private readonly LibraryStore appearanceStore;
    private readonly PreferencesStore store;private readonly Action changed;private readonly Func<Task>? update;
    private readonly StackPanel page=new(){Margin=new(28),MaxWidth=560,HorizontalAlignment=HorizontalAlignment.Left};
    private readonly ListBox sections=new(){Width=175,Margin=new(8),ItemsSource=new[]{"General","Formats","Compatibility","Folders","Updates","About"},SelectedIndex=0};
    internal string Section{get=>(string)sections.SelectedItem!;set=>sections.SelectedItem=value;}
    public SettingsWindow(PreferencesStore store,Action changed,Func<Task>? update,LibraryStore appearanceStore)
    {
        this.appearanceStore=appearanceStore;this.update=update;L10n.Choices(sections);
        this.store=store;this.changed=changed;Nashi.Linux.Theme.Attach(this);Title="Make it yours · Nashi";Width=850;Height=680;MinWidth=740;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel();Content=root;DockPanel.SetDock(sections,Dock.Left);root.Children.Add(sections);
        root.Children.Add(new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        sections.SelectionChanged+=(_,_)=>ShowSection();ShowSection();
        AddHandler(KeyDownEvent,(object? _,KeyEventArgs e)=>{if(e.Key==Key.Escape){Close();e.Handled=true;}},RoutingStrategies.Tunnel);
    }
    private void Fail(string message,string title){if(Services.Dialogs is not null)_=Services.Dialogs.InfoAsync(this,L10n.Text(message),L10n.Text(title));}
    private void Save(Preferences value){try{store.Save(value);changed();}catch(Exception error)when(error is IOException or ArgumentException or UnauthorizedAccessException){Fail(error.Message,"Settings could not be saved");}}
    private void Heading(string title,string hint){page.Children.Add(Ui.Text(title,28));var text=Ui.Text(hint,13,"Muted");text.Margin=new(0,8,0,20);page.Children.Add(text);}
    private void Caption(Panel target,string title){var text=Ui.Text(title,13);text.Margin=new(4,12,4,4);target.Children.Add(text);}
    private void Toggle(string title,bool value,Action<bool> save)
    {
        var box=L10n.Content(new CheckBox{Content=title,IsChecked=value,Margin=new(4,8,4,8)});
        box.IsCheckedChanged+=(_,_)=>save(box.IsChecked==true);page.Children.Add(box);
    }
    private void Pick(string title,string[] values,string value,Action<string> save)
    {
        Caption(page,title);var combo=new ComboBox{ItemsSource=values,SelectedItem=value};L10n.Choices(combo,new ChoiceTextConverter());
        combo.SelectionChanged+=(_,_)=>{if(combo.SelectedItem is string selected)save(selected);};page.Children.Add(combo);
    }
    private void LanguagePicker()
    {
        Caption(page,"Language");
        var language=new ComboBox{ItemsSource=new[]{"English","Türkçe"},SelectedIndex=store.Value.Language=="tr"?1:0};
        // Live: Save applies the language through changed(), and every bound label follows LanguageState.
        language.SelectionChanged+=(_,_)=>Save(store.Value with{Language=language.SelectedIndex==1?"tr":"en"});page.Children.Add(language);
    }
    private async Task ChooseFolderAsync(string title,Func<string,Preferences> apply)
    {
        if(Services.Files is null)return;
        var folder=await Services.Files.OpenFolderAsync(this,L10n.Text(title));
        if(folder is null)return;Save(apply(folder));ShowSection();
    }
    private void ShowSection()
    {
        page.Children.Clear();switch(sections.SelectedItem as string)
        {
            case "General":
                Heading("Your kind of flow.","A few small preferences. A smoother everyday.");LanguagePicker();
                Pick("Appearance",["system","light","dark"],appearanceStore.Appearance,v=>{try{appearanceStore.SetAppearance(v);Nashi.Linux.Theme.Apply(v);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){Fail(error.Message,"Appearance could not be saved");}});
                if(Services.Sound is {} sound){Toggle("Sound feedback",store.Value.Feedback,v=>Save(store.Value with{Feedback=v}));page.Children.Add(Ui.Button("Try feedback",()=>{var was=sound.Enabled;sound.Enabled=true;sound.Play("WheelSelection");sound.Enabled=was;}));}
                Toggle("Subtle animations",store.Value.Animations,v=>Save(store.Value with{Animations=v}));
                if(Services.Tray?.Available==true)Toggle("Keep running in the system tray",store.Value.KeepInTray,v=>Save(store.Value with{KeepInTray=v}));
                Pick("Wheel finish",["glass","solid"],store.Value.WheelFinish,v=>Save(store.Value with{WheelFinish=v}));
                Caption(page,"Wheel size");var size=new Slider{Minimum=.85,Maximum=1.4,Value=store.Value.WheelScale,Margin=new(4,0,4,8)};ToolTip.SetTip(size,"Wheel size");L10n.Attach(size);
                size.PropertyChanged+=(_,e)=>{if(e.Property==RangeBase.ValueProperty)Save(store.Value with{WheelScale=size.Value});};page.Children.Add(size);
                if(Services.Autostart is {} autostart)Toggle("Launch at login",autostart.Enabled,v=>{try{autostart.Set(v);}catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidOperationException){Fail(error.Message,"Launch at login");}});
                break;
            case "Formats":FormatDefaults();break;
            case "Compatibility":
                Heading("Everything has a next form.","The same conversion and tool catalog as macOS.");
                foreach(var pair in FeatureCatalog.Conversions.OrderBy(p=>p.Key))page.Children.Add(new TextBlock{Text=pair.Key.ToUpperInvariant()+"  →  "+string.Join(", ",pair.Value.Select(v=>v.ToUpperInvariant())),TextWrapping=Avalonia.Media.TextWrapping.Wrap,Margin=new(4,8,4,8)});
                break;
            case "Folders":
                Heading("A place for fresh files.","Originals remain safe. Choose where new copies should land.");
                Pick("Save new copies",["beside","ask","folder"],store.Value.OutputMode,v=>Save(store.Value with{OutputMode=v}));
                page.Children.Add(Ui.Button("Choose output folder…",async()=>await ChooseFolderAsync("Save new copies here",f=>store.Value with{OutputFolder=f,OutputMode="folder"})));page.Children.Add(new TextBlock{Text=store.Value.OutputFolder,TextWrapping=Avalonia.Media.TextWrapping.Wrap});
                page.Children.Add(Ui.Button("Choose downloads folder…",async()=>await ChooseFolderAsync("Save downloaded media here",f=>store.Value with{DownloadFolder=f})));page.Children.Add(new TextBlock{Text=store.Value.DownloadFolder,TextWrapping=Avalonia.Media.TextWrapping.Wrap});
                break;
            case "Updates":
                Heading("Fresh things, on their way.","Updates are downloaded from the public Nashi repository and verified before installation. No GitHub account or token is required.");
                Toggle("Check for updates automatically",store.Value.AutomaticUpdateChecks,v=>Save(store.Value with{AutomaticUpdateChecks=v}));
                if(update is not null)page.Children.Add(Ui.Button("Check for updates",async()=>{try{await update();}catch(Exception error){Fail(error.Message,"Update check failed");}}));
                break;
            default:
                Heading("Nashi","A little pear. A lot of possibility.");
                var version=Services.Updates?.Current??typeof(SettingsWindow).Assembly.GetName().Version;
                var about=Ui.Text("Local by design · MIT source",13);about.Margin=new(4,12,4,2);page.Children.Add(about);
                page.Children.Add(new TextBlock{Text="Version "+version?.ToString(3),Margin=new(4,0,4,12)});
                page.Children.Add(Ui.Button("Source & third-party notices",()=>Services.Shell?.OpenUrl("https://github.com/orkunisitmak/nashi-app")));
                break;
        }
    }
}
