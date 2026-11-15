using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Nashi.Core;
using Nashi.Linux.Platform;
namespace Nashi.Linux;

// Port of Nashi.Windows/MainWindow.Settings.cs (settings entry point, ApplyPreferences) and the tool setup entry of MainWindow.Library.cs.
// The Settings and Finish setup buttons live in the header, like Windows.
internal sealed partial class MainWindow
{
    private readonly MetadataService metadataService=new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Nashi","Tools"));
    private SettingsWindow? settingsWindow;
    private ToolSetupWindow? toolSetupWindow;
    private Button? toolsSetupButton,settingsButton;
    internal SettingsWindow? OpenSettings=>settingsWindow;
    internal ToolSetupWindow? OpenToolSetup=>toolSetupWindow;
    partial void InitSettings()
    {
        toolsSetupButton=Button("Finish setup",()=>ShowToolSetup());settingsButton=Button("Settings",()=>ShowSettings());
        foreach(var button in new[]{toolsSetupButton,settingsButton}){button.Margin=new(0,0,8,0);button.FontSize=11;AddHeaderButton(button);}
        L10n.SetTip(settingsButton,"Settings  ·  Ctrl+,");UpdateToolsSetupLabel();
        AddHandler(KeyDownEvent,(object? _,KeyEventArgs e)=>{if(!busy&&e.KeyModifiers==KeyModifiers.Control&&e.Key==Key.OemComma){ShowSettings();e.Handled=true;}},RoutingStrategies.Tunnel);
        ApplyPreferences();
    }
    private void ApplyPreferences()
    {
        L10n.Language=preferences.Value.Language;Nashi.Linux.Theme.Animations=preferences.Value.Animations;
        Feedback.Enabled=preferences.Value.Feedback;WheelPresentation.Preferences=preferences.Value;
    }
    // Same rule as Windows: "Tools ready" once media tools and the Office runtime are in place; Linux has no Office runtime until one is registered, so that part only counts when it exists.
    internal bool ToolsReady=>tools.Ready&&(Services.Office is null||Services.Office.Executable is not null);
    private void UpdateToolsSetupLabel(){if(toolsSetupButton is not null)L10n.Bind(toolsSetupButton,ContentControl.ContentProperty,ToolsReady?"Tools ready":"Finish setup");}
    // A separate window rather than a dialog, like Windows: the main window stays usable.
    internal SettingsWindow ShowSettings()
    {
        if(settingsWindow is null)
        {
            // The Updates section offers a manual release check; Linux has no self-update.
            settingsWindow=new(preferences,ApplyPreferences,CheckUpdatesNowAsync,library);
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
            toolSetupWindow.Closed+=(_,_)=>{toolSetupWindow=null;UpdateToolsSetupLabel();};
            toolSetupWindow.Show(this);
        }
        toolSetupWindow.Activate();return toolSetupWindow;
    }
}
