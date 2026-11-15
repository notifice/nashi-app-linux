using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Nashi.Core;
using Nashi.Linux.Platform;
namespace Nashi.Linux;

public enum WheelMode{Tools,Convert,Actions}
internal static class WheelPresentation
{
    internal const double Diameter=320;
    internal static double StartAngle(int count)=>count==3?-180:-90;
    internal static Preferences Preferences{get;set;}=new();
    internal static string Id(this WheelMode mode)=>mode switch{WheelMode.Convert=>"convert",WheelMode.Actions=>"actions",_=>"tools"};
    internal static IBrush Backdrop=>new SolidColorBrush(((ISolidColorBrush)Nashi.Linux.Theme.Brush("Surface")).Color,Preferences.WheelFinish=="glass"?.95:1);
    internal static double LabelRadius(int count,double half)=>count<=2?180:4*Math.Sin(half)*(Math.Pow(250,3)-Math.Pow(108,3))/(6*half*(250*250-108*108));
    internal static string ToolSymbol(string id)=>id switch{"compress"=>"↘","removeMetadata"=>"◇","editImage"=>"☷","annotateImage"=>"✎","frameImage"=>"▣","cropImage" or "cropVideo"=>"⌗","trimAudio" or "trimVideo"=>"✂","muteVideo"=>"♫","normalizeAudio"=>"≋","audioChannels"=>"⇆","audioToVideo"=>"▷","createCollage"=>"▦","redactImage" or "redactVideo" or "redactAudio"=>"▨","readQRCode"=>"▧","mergePDF"=>"▣","splitPDF" or "splitVideo"=>"◫","organizePDF"=>"▤","extractArchive"=>"↗","createPDF"=>"▱","joinVideos"=>"⇥","videoSnapshots"=>"▣","changeVideoSpeed"=>"◴",_=>"↗"};
    /// The segments for one mode: Tools (with Convert first when formats exist), Convert (one segment per target format) or File actions.
    internal static IReadOnlyList<ToolChoice> Choices(IReadOnlyList<string> files,string mode)
    {
        if(files.Count==0)return [];
        if(mode=="actions")return [new("share","Share","◉"),new("float","Float","▣"),new("copy","Copy","▱"),new("move","Move","↗"),new("copyPath","Copy Path","↪"),new("terminal","Terminal",">_")];
        if(mode=="convert")return FeatureCatalog.Formats(files).Select(format=>new ToolChoice("convert:"+format,format.ToUpperInvariant(),"↗")).ToArray();
        var tools=FeatureCatalog.Tools(files).Select(id=>new ToolChoice(id switch{"cropImage"=>"crop","annotateImage"=>"annotate","mergePDF"=>"mergePDFs",_=>id},FeatureCatalog.ToolNames[id],ToolSymbol(id))).ToList();
        if(FeatureCatalog.Formats(files).Length>0)tools.Insert(0,new("convert","Convert","⇄"));return tools;
    }
    internal static (StackPanel Label,Point Center) Caption(ToolChoice choice,Geometry petal,double angle,double half,int count)
    {
        var preferred=LabelRadius(count,half);
        for(var font=16d;font>=10;font-=.5)for(var width=142d;width>=72;width-=7)
        {
            var label=new StackPanel{Width=width,IsHitTestVisible=false};
            if(choice.Placement is {} placement)
            {
                var glyph=new Canvas{Width=36,Height=25,HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,0,0,7)};glyph.Children.Add(new Avalonia.Controls.Shapes.Rectangle{Width=35,Height=24,RadiusX=3,RadiusY=3,Stroke=Nashi.Linux.Theme.Brush("Ink"),StrokeThickness=1.7});
                var (start,end)=placement.Range();var fill=new Avalonia.Controls.Shapes.Rectangle{Width=29*(end-start),Height=18,Fill=Nashi.Linux.Theme.Brush("Ink"),Opacity=.65};Canvas.SetLeft(fill,3+29*start);Canvas.SetTop(fill,3);glyph.Children.Add(fill);label.Children.Add(glyph);
            }
            else label.Children.Add(new TextBlock{Text=choice.Symbol,FontSize=32,TextAlignment=TextAlignment.Center,Foreground=Nashi.Linux.Theme.Brush("Ink"),Margin=new(0,0,0,6)});
            label.Children.Add(new TextBlock{Text=L10n.Uppercase(choice.Title),FontSize=font,FontWeight=FontWeight.SemiBold,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,Foreground=Nashi.Linux.Theme.Brush("Ink")});
            label.Measure(new Size(width,double.PositiveInfinity));var h=label.DesiredSize.Height;
            for(var step=0;step<=20;step++)
            {
                var radial=preferred+(step==0?0:(step%2==1?1:-1)*((step+1)/2)*5);var center=new Point(270+radial*Math.Cos(angle),270+radial*Math.Sin(angle));var r=new Rect(center.X-width/2-3,center.Y-h/2-3,width+6,h+6);
                if(new[]{r.TopLeft,r.TopRight,r.BottomLeft,r.BottomRight,new Point(r.Left,r.Top+r.Height/2),new Point(r.Right,r.Top+r.Height/2)}.All(petal.FillContains))return(label,center);
            }
        }
        var fallback=new StackPanel{Width=72};fallback.Children.Add(new TextBlock{Text=L10n.Uppercase(choice.Title),FontSize=10,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,Foreground=Nashi.Linux.Theme.Brush("Ink")});fallback.Measure(new Size(72,double.PositiveInfinity));return(fallback,new Point(270+preferred*Math.Cos(angle),270+preferred*Math.Sin(angle)));
    }
    internal static bool Animated=>Preferences.Animations&&Services.Screen?.ReducedMotion!=true;
    // Fades the wheel in over 140 ms unless animations are off in Settings or the desktop asks for reduced motion.
    internal static void Animate(Control element,double from=0)
    {
        if(!Animated)return;
        element.Transitions=[new DoubleTransition{Property=Visual.OpacityProperty,Duration=TimeSpan.FromMilliseconds(140)}];
        element.Opacity=from;Avalonia.Threading.Dispatcher.UIThread.Post(()=>element.Opacity=1,Avalonia.Threading.DispatcherPriority.Render);
    }
}
