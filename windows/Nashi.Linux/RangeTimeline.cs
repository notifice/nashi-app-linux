using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
namespace Nashi.Linux;

// Port of Nashi.Windows/RangeTimeline.cs. Two child controls stand for the In and Out handles so each edge gets
// its own automation peer (a RangeValue slider in seconds), replacing the WPF virtual peers.
// Keys while the lane has focus: Left/Right move the playhead (Shift = fine step), I/O set In/Out at the playhead, Space asks for play/pause.
internal sealed class RangeTimeline:Panel
{
    internal const double MinimumSpan=.05;
    public double Duration{get;private set;}=1;public double Start{get;private set;}public double End{get;private set;}=1;public double Playhead{get;private set;}
    /// Seconds a plain Left/Right moves the playhead; Shift uses FineStep.
    public double SeekStep{get;set;}=1;public double FineStep{get;set;}=.1;
    public event Action<double,double>? Changed;
    public event Action<double>? PlayheadChanged;
    public event Action? PlayPauseRequested;
    private bool dragging,movePlayhead,moveRange;private bool? startHandle;private double originalStart,originalEnd,originX,laneWidth;
    private Bitmap[] artwork=[];
    private readonly RangeEndpoint startEndpoint,endEndpoint;private readonly RangePainter painter;
    private void Repaint(){painter.InvalidateVisual();InvalidateArrange();}
    public void SetArtwork(Bitmap[] images){artwork=images;Repaint();}
    public RangeTimeline()
    {
        Height=54;MinWidth=160;Cursor=new(StandardCursorType.SizeWestEast);Focusable=true;Background=Brushes.Transparent;
        AutomationProperties.SetAutomationId(this,"media.range.timeline");AutomationProperties.SetName(this,L10n.Text("Trim"));
        painter=new(this){IsHitTestVisible=false};AutomationProperties.SetAccessibilityView(painter,AccessibilityView.Raw);Children.Add(painter);startEndpoint=new(this,true);endEndpoint=new(this,false);Children.Add(startEndpoint);Children.Add(endEndpoint);
    }
    protected override AutomationPeer OnCreateAutomationPeer()=>new RangeTimelineAutomationPeer(this);
    public void Set(double duration,double start,double end)
    {
        Duration=double.IsFinite(duration)?Math.Max(MinimumSpan,duration):MinimumSpan;
        Start=double.IsFinite(start)?Math.Clamp(start,0,Duration-MinimumSpan):0;
        End=double.IsFinite(end)?Math.Clamp(end,Start+MinimumSpan,Duration):Duration;
        Playhead=Math.Clamp(Playhead,0,Duration);Repaint();
    }
    public void SetPlayhead(double seconds){var next=Math.Clamp(double.IsFinite(seconds)?seconds:0,0,Duration);if(next==Playhead)return;Playhead=next;Repaint();}
    internal bool HasValidRange=>double.IsFinite(Duration)&&double.IsFinite(Start)&&double.IsFinite(End)&&Duration>=MinimumSpan&&Start>=0&&End<=Duration&&End>Start;
    internal (double Minimum,double Maximum) EndpointBounds(bool start)=>!HasValidRange?(0,0):start?(0,Math.Max(0,End-MinimumSpan)):(Math.Min(Duration,Start+MinimumSpan),Duration);
    /// Semantic edits (automation, keys) enter the same Changed callback used by pointer drags.
    internal bool SetEndpoint(bool start,double value)
    {
        if(!IsEffectivelyEnabled||!HasValidRange||!double.IsFinite(value))return false;
        var limits=EndpointBounds(start);var bounded=Math.Clamp(value,limits.Minimum,limits.Maximum);
        var oldStart=Start;var oldEnd=End;if(start)Start=bounded;else End=bounded;
        if(Start==oldStart&&End==oldEnd)return false;
        Repaint();Changed?.Invoke(Start,End);return true;
    }
    /// Moves the playhead by a step and reports it.
    internal void MovePlayhead(double seconds){SetPlayhead(seconds);PlayheadChanged?.Invoke(Playhead);}
    private double Lane=>Math.Max(1,Bounds.Width-24);
    private double X(double seconds)=>12+Lane*seconds/Duration;
    protected override Size ArrangeOverride(Size finalSize)
    {
        var lane=Math.Max(1,finalSize.Width-24);
        painter.Arrange(new Rect(finalSize));startEndpoint.Arrange(new Rect(12+lane*Start/Duration-7,3,14,48));endEndpoint.Arrange(new Rect(12+lane*End/Duration-7,3,14,48));
        return finalSize;
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        painter.Measure(new Size(double.IsInfinity(availableSize.Width)?MinWidth:availableSize.Width,Height));startEndpoint.Measure(new Size(14,48));endEndpoint.Measure(new Size(14,48));
        return new Size(double.IsInfinity(availableSize.Width)?MinWidth:availableSize.Width,Height);
    }
    internal void Paint(DrawingContext dc)
    {
        var w=Lane;var track=new Rect(12,8,w,38);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(215,226,204)),null,new RoundedRect(track,8));
        if(artwork.Length>0)
        {
            using(dc.PushClip(new RoundedRect(track,8)))
                for(var n=0;n<artwork.Length;n++)dc.DrawImage(artwork[n],new Rect(0,0,artwork[n].PixelSize.Width,artwork[n].PixelSize.Height),new Rect(12+w*n/artwork.Length,8,w/artwork.Length,38));
        }
        var left=X(Start);var right=X(End);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(artwork.Length>0?(byte)70:(byte)255,229,189,80)),new Pen(Brushes.Gold,1),new RoundedRect(new Rect(left,8,Math.Max(4,right-left),38),6));
        foreach(var x in new[]{left,right}){dc.DrawRectangle(Brushes.DarkOliveGreen,null,new RoundedRect(new Rect(x-7,3,14,48),5));dc.DrawLine(new Pen(Brushes.White,2),new Point(x,19),new Point(x,35));}
        var head=X(Playhead);dc.DrawLine(new Pen(Brushes.White,3),new Point(head,4),new Point(head,50));dc.DrawLine(new Pen(Brushes.OrangeRed,1.5),new Point(head,4),new Point(head,50));
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);if(!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;
        Focus();var x=e.GetPosition(this).X;laneWidth=Lane;var a=X(Start);var b=X(End);
        moveRange=x>a+14&&x<b-14;movePlayhead=false;startHandle=null;
        if(!moveRange)
        {
            if(Math.Abs(x-a)<=14||Math.Abs(x-b)<=14)startHandle=Math.Abs(x-a)<=Math.Abs(x-b);
            else{movePlayhead=true;MovePlayhead((x-12)/laneWidth*Duration);}
        }
        originalStart=Start;originalEnd=End;originX=x;dragging=true;e.Pointer.Capture(this);e.Handled=true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);if(!dragging)return;var x=e.GetPosition(this).X;var delta=(x-originX)/laneWidth*Duration;
        if(movePlayhead){MovePlayhead((x-12)/laneWidth*Duration);e.Handled=true;return;}
        if(moveRange){var length=originalEnd-originalStart;Start=Math.Clamp(originalStart+delta,0,Duration-length);End=Start+length;}
        else if(startHandle==true)Start=Math.Clamp(originalStart+delta,0,originalEnd-MinimumSpan);
        else End=Math.Clamp(originalEnd+delta,originalStart+MinimumSpan,Duration);
        Repaint();Changed?.Invoke(Start,End);e.Handled=true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e){base.OnPointerReleased(e);dragging=false;e.Pointer.Capture(null);e.Handled=true;}
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e){dragging=false;base.OnPointerCaptureLost(e);}
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);if(e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift))return;
        var fine=e.KeyModifiers==KeyModifiers.Shift;
        switch(e.Key)
        {
            case Key.Left:MovePlayhead(Playhead-(fine?FineStep:SeekStep));break;
            case Key.Right:MovePlayhead(Playhead+(fine?FineStep:SeekStep));break;
            case Key.I:if(Playhead<End)SetEndpoint(true,Playhead);break;
            case Key.O:if(Playhead>Start)SetEndpoint(false,Playhead);break;
            case Key.Space:PlayPauseRequested?.Invoke();break;
            default:return;
        }
        e.Handled=true;
    }
}

/// Draws the lane for its timeline; Panel seals Render, so the picture lives in this first child.
internal sealed class RangePainter(RangeTimeline lane):Control
{
    public override void Render(DrawingContext context){base.Render(context);lane.Paint(context);}
    protected override Size MeasureOverride(Size availableSize)=>availableSize;
}

/// One handle of the lane. It paints nothing; the lane draws it, and this control carries the RangeValue peer.
internal sealed class RangeEndpoint:Control
{
    internal readonly RangeTimeline Lane;internal readonly bool IsStart;
    internal RangeEndpoint(RangeTimeline lane,bool start)
    {
        Lane=lane;IsStart=start;Width=14;Height=48;IsHitTestVisible=false;
        AutomationProperties.SetAutomationId(this,start?"media.range.0.start":"media.range.0.end");AutomationProperties.SetName(this,L10n.Text(start?"Start":"End"));AutomationProperties.SetHelpText(this,L10n.Text("Seconds"));
    }
    protected override AutomationPeer OnCreateAutomationPeer()=>new RangeEndpointAutomationPeer(this);
}

/// The lane is a group; its endpoint children are real sliders.
internal sealed class RangeTimelineAutomationPeer(RangeTimeline owner):ControlAutomationPeer(owner)
{
    protected override string GetClassNameCore()=>nameof(RangeTimeline);
    protected override AutomationControlType GetAutomationControlTypeCore()=>AutomationControlType.Group;
}

internal sealed class RangeEndpointAutomationPeer(RangeEndpoint owner):ControlAutomationPeer(owner),IRangeValueProvider
{
    private RangeTimeline Lane=>owner.Lane;
    private bool Start=>owner.IsStart;
    public bool IsReadOnly=>!Lane.IsEffectivelyEnabled||!Lane.HasValidRange;
    public double Value=>Lane.HasValidRange?(Start?Lane.Start:Lane.End):0;
    public double Minimum=>Lane.EndpointBounds(Start).Minimum;
    public double Maximum=>Lane.EndpointBounds(Start).Maximum;
    public double SmallChange=>.1;
    public double LargeChange=>1;
    public void SetValue(double value)
    {
        if(!Lane.IsEffectivelyEnabled)throw new InvalidOperationException("The timeline is disabled.");
        if(!Lane.HasValidRange)throw new InvalidOperationException("The source range is unavailable.");
        // RangeValue's contract rejects values outside the bounds it publishes.
        if(!double.IsFinite(value)||value<Minimum||value>Maximum)throw new ArgumentOutOfRangeException(nameof(value));
        Lane.SetEndpoint(Start,value);
    }
    protected override string GetClassNameCore()=>"RangeEndpoint";
    protected override AutomationControlType GetAutomationControlTypeCore()=>AutomationControlType.Slider;
}
