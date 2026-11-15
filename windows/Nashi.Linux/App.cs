using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
namespace Nashi.Linux;

internal sealed class App:Application
{
    // Brush keys mirror Nashi.Windows/App.xaml and Theme.cs:17. (key, light, dark)
    internal static readonly (string Key,string Light,string Dark)[] Palette=[("Paper","#F9F6ED","#111A14"),("Ink","#243B2B","#E7EBE6"),("Surface","#FFFEF7","#1C281F"),("Inset","#F1EDE0","#151F18"),("Muted","#596650","#A9B5A8"),("Line","#DCDDD1","#344237"),("Sage","#E0EBD2","#243928"),("Sand","#F6E7BE","#3C3421"),("Gold","#E8BF59","#E8BF59")];
    public override void Initialize()
    {
        // Nashi gold instead of the Fluent blue for checkboxes, sliders, selection and focus; Fluent derives the light/dark shades from Accent.
        var fluent=new FluentTheme();
        foreach(var variant in new[]{ThemeVariant.Light,ThemeVariant.Dark})fluent.Palettes[variant]=new ColorPaletteResources{Accent=Color.Parse("#E8BF59")};
        Styles.Add(fluent);
        ApplyPalette(dark:true);
    }
    internal static void ApplyPalette(bool dark)
    {
        var app=Current!;app.RequestedThemeVariant=dark?ThemeVariant.Dark:ThemeVariant.Light;
        // Dark olive text on the gold accent, like the Choose files button.
        foreach(var key in new[]{"TextOnAccentFillColorPrimaryBrush","TextOnAccentFillColorSecondaryBrush","TextOnAccentFillColorDisabledBrush"})app.Resources[key]=new SolidColorBrush(Color.FromRgb(43,54,28));
        foreach(var (key,light,darkValue) in Palette)app.Resources[key]=new SolidColorBrush(Color.Parse(dark?darkValue:light));
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)desktop.MainWindow=new MainWindow(desktop.Args??[]);
        base.OnFrameworkInitializationCompleted();
    }
}
