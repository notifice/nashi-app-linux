using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Path=Avalonia.Controls.Shapes.Path;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Nashi.Core;
using Nashi.Linux.Platform;
namespace Nashi.Linux;

public sealed record ToolChoice(string Id,string Title,string Symbol,WindowPlacement? Placement=null);
public sealed class WheelWindow:Window
{
    // Smoke scenes run several windows back to back; a focus hand-off between them must not dismiss the wheel.
    internal static bool KeepOpenOnDeactivateForTest;
    private readonly List<Button> buttons=[];
    private readonly List<Path> sectors=[];
    private IReadOnlyList<ToolChoice> choices;
    private readonly Action<ToolChoice>? select;
    private readonly Canvas surface;
    private readonly Window? ownerWindow;
    private readonly TaskCompletionSource<ToolChoice?> done=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int selected=-1;
    internal int SelectedIndex=>selected;
    /// Opens the wheel for the files in the given mode and returns the picked choice, or null when it was dismissed or has nothing to offer.
    /// Ids are the ones Nashi.Windows uses: "convert:jpg", "convert", a tool id such as "compress", or a file action such as "share".
    public static Task<ToolChoice?> ShowAsync(Window? owner,IReadOnlyList<string> files,WheelMode mode)
    {
        var initial=mode.Id();var first=WheelPresentation.Choices(files,initial);
        if(first.Count==0)return Task.FromResult<ToolChoice?>(null);
        return new WheelWindow(owner,first,modes:m=>WheelPresentation.Choices(files,m),initialMode:initial).ShowAsync();
    }
    /// Shows this wheel; completes with the choice, or null on Escape, clicking the centre or losing focus.
    public Task<ToolChoice?> ShowAsync()
    {
        if(ownerWindow is not null)Show(ownerWindow);else Show();
        return done.Task;
    }
    internal void HoverScreen(PixelPoint point)=>Hover(surface.PointToClient(point));
    internal void Hover(Point local)
    {
        var delta=local-new Point(270,270);var distance=Math.Sqrt(delta.X*delta.X+delta.Y*delta.Y);
        if(distance<108||distance>250){Highlight(-1);return;}
        var angle=(Math.Atan2(delta.Y,delta.X)*180/Math.PI-WheelPresentation.StartAngle(choices.Count)+360)%360;
        var index=(int)Math.Floor((angle+180d/choices.Count)%360/(360d/choices.Count));
        Highlight(index);
    }
    internal bool Navigate(Key key)
    {
        if(key==Key.Escape){Close();return true;}
        if(buttons.Count==0)return false;
        if(key is Key.Right or Key.Down){Move(1);return true;}
        if(key is Key.Left or Key.Up){Move(-1);return true;}
        if(key is Key.Enter or Key.Space){Pick(choices[Math.Max(0,selected)]);return true;}
        return false;
    }
    private void Move(int step){Highlight((selected+step+buttons.Count)%buttons.Count);buttons[selected].Focus();}
    private void Pick(ToolChoice choice){if(done.Task.IsCompleted)return;done.TrySetResult(choice);Close();select?.Invoke(choice);}
    private void Highlight(int index)
    {
        if(selected==index)return;selected=index;
        for(var i=0;i<sectors.Count;i++){sectors[i].Fill=Nashi.Linux.Theme.Brush(i==index?"Sage":"Inset");sectors[i].Stroke=Nashi.Linux.Theme.Brush(i==index?"Gold":"Line");sectors[i].StrokeThickness=i==index?2:1;}
        if(index>=0)Feedback.Play();
    }
    private readonly bool externalDrag;private readonly Func<string,IReadOnlyList<ToolChoice>>? modes;private string mode="tools";
    /// Ctrl, Shift and Alt switch between Tools, Convert and File actions (as configured in Settings), like the wheel that opens while dragging.
    internal bool SwitchMode(string modifier,IReadOnlyCollection<string> held)
    {
        if(modes is null)return false;var preferences=WheelPresentation.Preferences;string? best=null;var size=0;
        foreach(var (kind,combo) in new[]{("tools",preferences.ToolsModifier),("convert",preferences.ConvertModifier),("actions",preferences.ActionsModifier)})
        {var set=combo.Split('+');if(set.Contains(modifier)&&set.All(m=>m==modifier||held.Contains(m))&&set.Length>size){best=kind;size=set.Length;}}
        if(best is null||best==mode)return false;var next=modes(best);if(next.Count==0)return false;mode=best;Populate(next);return true;
    }
    internal string Mode=>mode;
    private static string? ModifierName(Key key)=>key switch{Key.LeftCtrl or Key.RightCtrl=>"Ctrl",Key.LeftShift or Key.RightShift=>"Shift",Key.LeftAlt or Key.RightAlt=>"Alt",_=>null};
    private static List<string> HeldModifiers(KeyModifiers modifiers)
    {
        var held=new List<string>();if(modifiers.HasFlag(KeyModifiers.Control))held.Add("Ctrl");if(modifiers.HasFlag(KeyModifiers.Shift))held.Add("Shift");if(modifiers.HasFlag(KeyModifiers.Alt))held.Add("Alt");return held;
    }
    // Transparent, chrome-free button face, like Nashi.Windows' WheelLabelButton style.
    private static readonly FuncControlTemplate<Button> LabelTemplate=new((parent,_)=>new Border{Background=Brushes.Transparent,Child=new ContentPresenter{HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Center,VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center,[!ContentPresenter.ContentProperty]=parent[!ContentControl.ContentProperty]}});
    private static Button LabelButton(Control content,double width,double height)=>new(){Template=LabelTemplate,Content=content,Width=width,Height=height,Background=Brushes.Transparent,Foreground=Nashi.Linux.Theme.Brush("Ink"),Padding=new(0),BorderThickness=new(0),Cursor=new Cursor(StandardCursorType.Hand)};
    public WheelWindow(Window? owner,IReadOnlyList<ToolChoice> tools,Action<ToolChoice>? select=null,bool externalDrag=false,Func<string,IReadOnlyList<ToolChoice>>? modes=null,string initialMode="tools")
    {
        mode=initialMode;choices=tools;this.select=select;this.externalDrag=externalDrag;this.modes=modes;ownerWindow=owner;
        Title=externalDrag?"Nashi Window Management":"Nashi tools";Width=WheelPresentation.Diameter*WheelPresentation.Preferences.WheelScale;Height=Width;WindowDecorations=WindowDecorations.None;TransparencyLevelHint=[WindowTransparencyLevel.Transparent];Background=Brushes.Transparent;CanResize=false;ShowInTaskbar=false;WindowStartupLocation=externalDrag?WindowStartupLocation.Manual:WindowStartupLocation.CenterOwner;
        if(externalDrag){ShowActivated=false;Topmost=true;Focusable=false;}
        var canvas=new Canvas{Width=540,Height=540};surface=canvas;KeyboardNavigation.SetTabNavigation(canvas,KeyboardNavigationMode.Cycle);var view=new Viewbox{Child=canvas,Stretch=Stretch.Uniform};Content=view;
        var disc=new Ellipse{Width=530,Height=530,Fill=WheelPresentation.Backdrop};Canvas.SetLeft(disc,5);Canvas.SetTop(disc,5);canvas.Children.Add(disc);
        Populate(tools);
        AddHandler(KeyDownEvent,(_,e)=>{if(modes is not null&&ModifierName(e.Key) is {} modifier){var held=HeldModifiers(e.KeyModifiers);if(!held.Contains(modifier))held.Add(modifier);if(SwitchMode(modifier,held)){e.Handled=true;return;}}e.Handled=Navigate(e.Key);},RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent,(_,e)=>{if(buttons.Count==0||e.Delta.Y==0)return;Move(e.Delta.Y<0?1:-1);e.Handled=true;},RoutingStrategies.Tunnel);
        bool closing=false,activated=false;
        Opened+=(_,_)=>{WheelPresentation.Animate(view);if(!externalDrag&&buttons.Count>0)buttons[0].Focus();};Activated+=(_,_)=>activated=true;
        Closing+=(_,_)=>closing=true;Closed+=(_,_)=>done.TrySetResult(null);Deactivated+=(_,_)=>{if(!externalDrag&&!KeepOpenOnDeactivateForTest&&activated&&!closing&&IsVisible)Close();};
    }
    // Builds the segments and the centre label for one set of choices; a mode switch calls it again on the same canvas.
    private void Populate(IReadOnlyList<ToolChoice> tools)
    {
        choices=tools;selected=-1;buttons.Clear();sectors.Clear();var canvas=surface;while(canvas.Children.Count>1)canvas.Children.RemoveAt(1);
        var count=Math.Min(12,tools.Count);
        for(var i=0;i<count;i++)
        {
            var choice=tools[i];var angle=(WheelPresentation.StartAngle(count)+360d*i/count)*Math.PI/180;var half=(180d/count-1)*Math.PI/180;var a=angle-half;var b=angle+half;
            Point P(double r,double v)=>new(270+r*Math.Cos(v),270+r*Math.Sin(v));
            var geometry=new StreamGeometry();using(var g=geometry.Open()){g.BeginFigure(P(108,a),true);g.LineTo(P(250,a));g.ArcTo(P(250,b),new Size(250,250),0,b-a>Math.PI,SweepDirection.Clockwise);g.LineTo(P(108,b));g.ArcTo(P(108,a),new Size(108,108),0,b-a>Math.PI,SweepDirection.CounterClockwise);g.EndFigure(true);}
            var path=new Path{Data=geometry,Fill=Nashi.Linux.Theme.Brush("Inset"),Stroke=Nashi.Linux.Theme.Brush("Line"),StrokeThickness=1};canvas.Children.Add(path);sectors.Add(path);
            var (label,location)=WheelPresentation.Caption(choice,geometry,angle,half,count);
            var button=LabelButton(label,label.DesiredSize.Width,label.DesiredSize.Height);Canvas.SetLeft(button,location.X-button.Width/2);Canvas.SetTop(button,location.Y-button.Height/2);canvas.Children.Add(button);buttons.Add(button);var index=i;
            button.Click+=(_,_)=>Pick(choice);button.PointerEntered+=(_,_)=>Highlight(index);button.GotFocus+=(_,_)=>Highlight(index);
            path.PointerReleased+=(_,e)=>{if(e.InitialPressMouseButton==MouseButton.Left)Pick(choice);};path.PointerEntered+=(_,_)=>Highlight(index);
        }
        var centerLabel=new StackPanel{VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center};centerLabel.Children.Add(L10n.Label(new TextBlock{Text=externalDrag?"Arrange window":tools.All(t=>t.Id.StartsWith("convert:"))?"Convert":tools.Any(t=>t.Id=="float")?"File actions":"Tools",FontSize=externalDrag?17:20,FontWeight=FontWeight.SemiBold,TextAlignment=TextAlignment.Center}));centerLabel.Children.Add(L10n.Label(new TextBlock{Text=externalDrag?"Release to place · Esc cancels":"Esc to close",FontSize=externalDrag?9:11,TextAlignment=TextAlignment.Center,Foreground=Nashi.Linux.Theme.Brush("Muted"),Margin=new(0,6,0,0)}));
        var center=LabelButton(centerLabel,180,180);center.Focusable=false;Canvas.SetLeft(center,180);Canvas.SetTop(center,180);canvas.Children.Add(center);center.Click+=(_,_)=>Close();
        if(IsLoaded&&!externalDrag&&buttons.Count>0)buttons[0].Focus();
    }
}
internal static class Feedback
{
    private const string Asset="avares://Nashi/Assets/WheelSelection.wav";
    private static bool enabled=true;
    internal static bool Enabled{get=>enabled;set{enabled=value;if(Services.Sound is {} sound)sound.Enabled=value;}}
    private static DateTime last;
    internal static void Stop()=>Services.Sound?.Stop();
    public static void Play()
    {
        if(!Enabled||Services.Sound is not {} sound||(DateTime.UtcNow-last).TotalMilliseconds<70)return;last=DateTime.UtcNow;
        sound.Play(Asset);
    }
}
