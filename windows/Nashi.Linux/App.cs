using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
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
        Styles.Add(fluent);Styles.AddRange(NashiStyles());
        ApplyPalette(dark:true);
    }
    internal static void ApplyPalette(bool dark)
    {
        var app=Current!;app.RequestedThemeVariant=dark?ThemeVariant.Dark:ThemeVariant.Light;
        // Dark olive text on the gold accent, like the Choose files button.
        foreach(var key in new[]{"TextOnAccentFillColorPrimaryBrush","TextOnAccentFillColorSecondaryBrush","TextOnAccentFillColorDisabledBrush"})app.Resources[key]=new SolidColorBrush(Color.FromRgb(43,54,28));
        foreach(var (key,light,darkValue) in Palette)app.Resources[key]=new SolidColorBrush(Color.Parse(dark?darkValue:light));
        // Fluent control resources pointed at the palette, so every Button/TextBox/ComboBox/list follows App.xaml: surface fill, line border, ink text, sage selection, gold focus.
        SolidColorBrush Of(string key,double opacity=1){var color=((SolidColorBrush)app.Resources[key]!).Color;return new SolidColorBrush(color,opacity);}
        foreach(var state in new[]{"","PointerOver","Pressed","Disabled"})
        {
            app.Resources["ButtonBackground"+state]=Of("Surface");app.Resources["ButtonBorderBrush"+state]=Of("Line");app.Resources["ButtonForeground"+state]=Of("Ink");
            app.Resources["ComboBoxBackground"+state]=Of("Surface");app.Resources["ComboBoxForeground"+state]=Of("Ink");app.Resources["ComboBoxBorderBrush"+state]=Of("Line");
        }
        foreach(var state in new[]{"","PointerOver","Disabled"})
        {
            app.Resources["TextControlBackground"+state]=Of("Inset");app.Resources["TextControlForeground"+state]=Of("Ink");app.Resources["TextControlBorderBrush"+state]=Of("Line");
        }
        app.Resources["TextControlBackgroundFocused"]=Of("Inset");app.Resources["TextControlForegroundFocused"]=Of("Ink");app.Resources["TextControlBorderBrushFocused"]=Of("Gold");
        app.Resources["ComboBoxBorderBrushFocused"]=Of("Gold");app.Resources["ComboBoxBorderBrushFocusedPointerOver"]=Of("Gold");
        app.Resources["TextControlPlaceholderForeground"]=Of("Muted");app.Resources["TextControlPlaceholderForegroundPointerOver"]=Of("Muted");app.Resources["TextControlPlaceholderForegroundFocused"]=Of("Muted");
        app.Resources["TextControlBorderThemeThickness"]=new Thickness(1);app.Resources["TextControlBorderThemeThicknessFocused"]=new Thickness(1);
        app.Resources["ComboBoxDropDownBackground"]=Of("Surface");app.Resources["ComboBoxDropDownBorderBrush"]=Of("Line");
        foreach(var state in new[]{"","PointerOver","Pressed","Selected","SelectedPointerOver","SelectedPressed"})
        {
            var fill=Of("Sage",state.Length==0?0:1);
            app.Resources["ComboBoxItemBackground"+state]=fill;app.Resources["ListBoxItemBackground"+state]=fill;
            app.Resources["ComboBoxItemForeground"+state]=Of("Ink");app.Resources["ListBoxItemForeground"+state]=Of("Ink");
        }
        foreach(var name in new[]{"SystemControlHighlightListAccentLowBrush","SystemControlHighlightListAccentMediumBrush","SystemControlHighlightListAccentHighBrush","SystemControlHighlightListLowBrush","SystemControlHighlightListMediumBrush"})app.Resources[name]=Of("Sage");
        foreach(var state in new[]{"","Pointerover","PointerOver","Pressed"}){app.Resources["ScrollBarThumbFill"+state]=Of("Muted",.45);}
        foreach(var state in new[]{"","PointerOver","Pressed"}){app.Resources["ExpanderHeaderBackground"+state]=Brushes.Transparent;app.Resources["ExpanderHeaderBorderBrush"+state]=Brushes.Transparent;app.Resources["ExpanderHeaderForeground"+state]=Of("Ink");}
        app.Resources["ExpanderContentBackground"]=Brushes.Transparent;app.Resources["ExpanderContentBorderBrush"]=Brushes.Transparent;
        app.Resources["ScrollBarTrackFill"]=Brushes.Transparent;app.Resources["ScrollBarTrackFillPointerOver"]=Brushes.Transparent;
    }
    static object Res(string key)=>new DynamicResourceExtension(key);
    // Shapes and states from Nashi.Windows/App.xaml: pill buttons (radius 13, padding 15,10, semibold), 10 px inputs, 0.85/0.65 opacity on hover/press, gold focus ring.
    static IEnumerable<IStyle> NashiStyles()
    {
        static Style Rule(Func<Selector?,Selector> selector,params Setter[] setters){var style=new Style(selector);foreach(var setter in setters)style.Setters.Add(setter);return style;}
        static Setter S(AvaloniaProperty property,object? value)=>new(property,value);
        static Selector Presenter(Selector? button)=>button!.Template().OfType<ContentPresenter>().Name("PART_ContentPresenter");
        yield return Rule(x=>x.OfType<Button>(),S(TemplatedControl.PaddingProperty,new Thickness(15,10)),S(Layoutable.MarginProperty,new Thickness(4)),S(TemplatedControl.CornerRadiusProperty,new CornerRadius(13)),S(TemplatedControl.FontWeightProperty,FontWeight.SemiBold),S(InputElement.CursorProperty,new Cursor(StandardCursorType.Hand)),S(Layoutable.MinHeightProperty,0.0));
        yield return Rule(x=>Presenter(x.OfType<Button>()),S(ContentPresenter.CornerRadiusProperty,new CornerRadius(13)));
        yield return Rule(x=>Presenter(x.OfType<Button>().Class(":pointerover")),S(Visual.OpacityProperty,.85));
        yield return Rule(x=>Presenter(x.OfType<Button>().Class(":pressed")),S(Visual.OpacityProperty,.65));
        yield return Rule(x=>x.OfType<Button>().Class(":disabled"),S(Visual.OpacityProperty,.4));
        yield return Rule(x=>Presenter(x.OfType<Button>().Class(":focus-visible")),S(ContentPresenter.BorderBrushProperty,Res("Gold")),S(ContentPresenter.BorderThicknessProperty,new Thickness(2)));
        // "primary" (gold gradient with olive text) and "active" (sage, the current header page) keep their fill in every state.
        foreach(var state in new[]{"",":pointerover",":pressed"})
        {
            yield return Rule(x=>Presenter(Class(x.OfType<Button>(),"primary",state)),S(ContentPresenter.BackgroundProperty,Theme.Primary),S(ContentPresenter.ForegroundProperty,new SolidColorBrush(Color.FromRgb(43,54,28))));
            yield return Rule(x=>Presenter(Class(x.OfType<Button>(),"active",state)),S(ContentPresenter.BackgroundProperty,Res("Sage")));
        }
        // A button that is itself a Home card: 24 px radius, gold outline on hover.
        yield return Rule(x=>x.OfType<Button>().Class("card"),S(TemplatedControl.CornerRadiusProperty,new CornerRadius(24)),S(TemplatedControl.FontWeightProperty,FontWeight.Normal));
        yield return Rule(x=>Presenter(x.OfType<Button>().Class("card")),new Setter(ContentPresenter.CornerRadiusProperty,new CornerRadius(24)));
        foreach(var state in new[]{":pointerover",":pressed"})yield return Rule(x=>Presenter(x.OfType<Button>().Class("card").Class(state)),S(Visual.OpacityProperty,1.0),S(ContentPresenter.BorderBrushProperty,Res("Gold")));
        yield return Rule(x=>x.OfType<TextBox>(),S(TemplatedControl.PaddingProperty,new Thickness(11,9)),S(Layoutable.MarginProperty,new Thickness(4)),S(TemplatedControl.CornerRadiusProperty,new CornerRadius(10)),S(Layoutable.MinHeightProperty,0.0),S(TextBox.VerticalContentAlignmentProperty,VerticalAlignment.Center));
        yield return Rule(x=>x.OfType<ComboBox>(),S(TemplatedControl.PaddingProperty,new Thickness(12,9,10,9)),S(Layoutable.MarginProperty,new Thickness(4)),S(TemplatedControl.CornerRadiusProperty,new CornerRadius(10)),S(Layoutable.MinWidthProperty,110.0),S(Layoutable.MinHeightProperty,0.0));
        yield return Rule(x=>x.OfType<ComboBoxItem>(),S(TemplatedControl.CornerRadiusProperty,new CornerRadius(6)),S(TemplatedControl.PaddingProperty,new Thickness(10,8)));
        yield return Rule(x=>x.OfType<ListBoxItem>(),S(TemplatedControl.CornerRadiusProperty,new CornerRadius(8)),S(TemplatedControl.PaddingProperty,new Thickness(8)));
        yield return Rule(x=>x.OfType<ListBox>(),S(TemplatedControl.BackgroundProperty,Brushes.Transparent),S(TemplatedControl.BorderThicknessProperty,new Thickness(0)));
        static Selector Class(Selector? selector,string name,string state)=>state.Length==0?selector.Class(name):selector.Class(name).Class(state);
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args=desktop.Args??[];var window=new MainWindow(args);
            // The wheel shortcut starting Nashi: the lifetime would show its MainWindow, so the window is kept unassigned and hidden, and closing it (once shown) still quits.
            if(args.Contains("--wheel")){desktop.ShutdownMode=ShutdownMode.OnExplicitShutdown;window.Closed+=(_,_)=>desktop.Shutdown();}else desktop.MainWindow=window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
