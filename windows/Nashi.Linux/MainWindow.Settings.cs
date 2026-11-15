using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Nashi.Core;
using Nashi.Linux.Platform;
namespace Nashi.Linux;

// Port of Nashi.Windows/MainWindow.Settings.cs (settings entry point, ApplyPreferences) and the tool setup entry of MainWindow.Library.cs.
internal sealed partial class MainWindow
{
    private readonly MetadataService metadataService=new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Nashi","Tools"));
    private SettingsWindow? settingsWindow;
    private ToolSetupWindow? toolSetupWindow;
    internal SettingsWindow? OpenSettings=>settingsWindow;
    internal ToolSetupWindow? OpenToolSetup=>toolSetupWindow;
    partial void InitSettings()
    {
        // MainWindow.cs keeps its header controls in a local, so the entry buttons sit in the Tools & shortcuts row below the home cards.
        var row=new WrapPanel{Margin=new(4,0,4,0)};
        row.Children.Add(Ui.Button("Settings",()=>ShowSettings()));row.Children.Add(Ui.Button("Finish setup",()=>ShowToolSetup()));
        foreach(var button in row.Children.OfType<Button>()){button.Margin=new(0,0,8,0);button.Padding=new(12,7,12,7);button.FontSize=12;}
        home.Children.Add(row);
        AddHandler(KeyDownEvent,(object? _,KeyEventArgs e)=>{if(!busy&&e.KeyModifiers==KeyModifiers.Control&&e.Key==Key.OemComma){ShowSettings();e.Handled=true;}},RoutingStrategies.Tunnel);
        ApplyPreferences();
    }
    private void ApplyPreferences()
    {
        L10n.Language=preferences.Value.Language;Nashi.Linux.Theme.Animations=preferences.Value.Animations;
        if(Services.Sound is {} sound)sound.Enabled=preferences.Value.Feedback;
    }
    // A separate window rather than a dialog, like Windows: the main window stays usable.
    internal SettingsWindow ShowSettings()
    {
        if(settingsWindow is null)
        {
            // Update checks arrive with P4-D; until then the Updates section has no button.
            settingsWindow=new(preferences,ApplyPreferences,null,library);
            settingsWindow.Closed+=(_,_)=>{settingsWindow=null;appearance.SelectedItem=char.ToUpper(library.Appearance[0])+library.Appearance[1..];};
            settingsWindow.Show(this);
        }
        settingsWindow.Activate();return settingsWindow;
    }
    internal ToolSetupWindow ShowToolSetup()
    {
        if(toolSetupWindow is null)
        {
            toolSetupWindow=new(tools,metadataService,Services.Office,SetBusy);
            toolSetupWindow.Closed+=(_,_)=>toolSetupWindow=null;
            toolSetupWindow.Show(this);
        }
        toolSetupWindow.Activate();return toolSetupWindow;
    }
}
