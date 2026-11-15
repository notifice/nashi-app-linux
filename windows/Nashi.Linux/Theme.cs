using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Threading;
using Nashi.Linux.Platform;
namespace Nashi.Linux;

internal static class Theme
{
    internal static bool Dark{get;private set;}
    // Windows reads WheelPresentation.Preferences.Animations; here MainWindow publishes the preference.
    internal static bool Animations{get;set;}=true;
    internal static Brush Primary=>new LinearGradientBrush{StartPoint=new(0,.5,RelativeUnit.Relative),EndPoint=new(1,.5,RelativeUnit.Relative),GradientStops=[new(Color.FromRgb(227,184,89),0),new(Color.FromRgb(245,209,125),1)]};
    internal static Brush Brush(string name)=>(Brush)Application.Current!.FindResource(name)!;
    internal static void Apply(string appearance)
    {
        Dark=appearance=="dark"||(appearance=="system"&&Services.Theme?.Dark==true);
        App.ApplyPalette(Dark);
    }
    internal static void Res(AvaloniaObject target,AvaloniaProperty property,string key)=>target[!property]=new DynamicResourceExtension(key);
    internal static void Attach(Window window)
    {
        Res(window,TemplatedControl.BackgroundProperty,"Paper");Res(window,TemplatedControl.ForegroundProperty,"Ink");
        window.Opened+=(_,_)=>{if(window.WindowDecorations!=WindowDecorations.None)Services.Screen?.FitToWorkArea(window);L10n.Bind(window,Window.TitleProperty,window.Title??"Nashi");L10n.Attach(window);};
    }
    internal static void Entrance(Control element)
    {
        if(!Animations||Services.Screen?.ReducedMotion==true)return;
        element.Transitions=[new DoubleTransition{Property=Visual.OpacityProperty,Duration=TimeSpan.FromMilliseconds(180)},new TransformOperationsTransition{Property=Visual.RenderTransformProperty,Duration=TimeSpan.FromMilliseconds(180),Easing=new Avalonia.Animation.Easings.CubicEaseOut()}];
        element.Opacity=0;element.RenderTransform=TransformOperations.Parse("translateY(8px)");
        Dispatcher.UIThread.Post(()=>{element.Opacity=1;element.RenderTransform=TransformOperations.Parse("translateY(0px)");},DispatcherPriority.Render);
    }
}
