using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Nashi.Engine;
namespace Nashi.Linux;
// Port of the redactImage part of Nashi.Windows/PhotoEditorOptions.cs and ToolEditor.cs (text and face detection are not ported).
// Areas are dragged on the preview in image pixels and listed with a remove button.
internal sealed partial class ToolEditor
{
    private readonly List<RectD> redactions=[];private readonly List<Rectangle> redactionShapes=[];
    private readonly ComboBox redactionStyle=new(){ItemsSource=new[]{"solid","blur","pixelate"},SelectedIndex=0};
    private readonly ListBox redactionList=new(){MaxHeight=220};
    private Rectangle? redactionDraft;private Point redactionOrigin;
    private void BuildRedactionOptions()
    {
        if(tool!="redactImage")return;
        options.Children.Add(L10n.Label(new TextBlock{Text="Drag on the photo to cover an area. Add as many areas as you need.",TextWrapping=TextWrapping.Wrap,Margin=new(4,4,4,8)}));
        options.Children.Add(L10n.Label(new TextBlock{Text="Cover style",Margin=new(4,12,4,0)}));options.Children.Add(redactionStyle);redactionStyle.SelectionChanged+=(_,_)=>{foreach(var shape in redactionShapes)shape.Fill=RedactionFill();};
        options.Children.Add(L10n.Label(new TextBlock{Text="Areas",Margin=new(4,12,4,4)}));options.Children.Add(redactionList);
        var buttons=new WrapPanel();buttons.Children.Add(Ui.Button("Remove selected",()=>RemoveRedaction(redactionList.SelectedIndex)));buttons.Children.Add(Ui.Button("Undo area",()=>RemoveRedaction(redactions.Count-1)));options.Children.Add(buttons);
        resetPhotoView=()=>{redactionDraft=null;redactions.Clear();redactionShapes.Clear();redactionList.Items.Clear();redactionStyle.SelectedIndex=0;};
        // EndDraw marks releases handled, so these handlers also listen for handled events.
        drawing.AddHandler(PointerPressedEvent,(_,e)=>
        {
            if(!e.GetCurrentPoint(drawing).Properties.IsLeftButtonPressed)return;drawing.Focus();StartRedaction(Clamp(e.GetPosition(drawing)));e.Pointer.Capture(drawing);e.Handled=true;
        },RoutingStrategies.Bubble,true);
        drawing.AddHandler(PointerMovedEvent,(_,e)=>{if(redactionDraft is not null)ExtendRedaction(Clamp(e.GetPosition(drawing)));},RoutingStrategies.Bubble,true);
        drawing.AddHandler(PointerReleasedEvent,(_,_)=>{if(redactionDraft is not null)FinishRedaction();},RoutingStrategies.Bubble,true);
    }
    private IBrush RedactionFill()=>redactionStyle.SelectedItem as string=="solid"?Brushes.Black:new SolidColorBrush(Color.FromArgb(150,224,189,82));
    private void StartRedaction(Point at)
    {
        redactionOrigin=at;redactionDraft=new Rectangle{Stroke=Brushes.Gold,StrokeThickness=Math.Max(2,drawing.Width/350),Fill=RedactionFill(),IsHitTestVisible=false};
        drawing.Children.Add(redactionDraft);Canvas.SetLeft(redactionDraft,at.X);Canvas.SetTop(redactionDraft,at.Y);
    }
    private void ExtendRedaction(Point p)
    {
        if(redactionDraft is null)return;
        Canvas.SetLeft(redactionDraft,Math.Min(redactionOrigin.X,p.X));Canvas.SetTop(redactionDraft,Math.Min(redactionOrigin.Y,p.Y));redactionDraft.Width=Math.Abs(p.X-redactionOrigin.X);redactionDraft.Height=Math.Abs(p.Y-redactionOrigin.Y);
    }
    private void FinishRedaction()
    {
        var shape=redactionDraft;redactionDraft=null;if(shape is null)return;
        var x=Math.Floor(Canvas.GetLeft(shape));var y=Math.Floor(Canvas.GetTop(shape));var w=Math.Min(Math.Floor(shape.Width),drawing.Width-x);var h=Math.Min(Math.Floor(shape.Height),drawing.Height-y);
        if(!(w>=1&&h>=1)){drawing.Children.Remove(shape);return;}
        shape.Width=w;shape.Height=h;Canvas.SetLeft(shape,x);Canvas.SetTop(shape,y);redactions.Add(new(x,y,w,h));redactionShapes.Add(shape);ListRedactions();
    }
    private void ListRedactions()
    {
        redactionList.Items.Clear();
        for(var n=0;n<redactions.Count;n++){var r=redactions[n];redactionList.Items.Add(new ListBoxItem{Content=$"{n+1}. {r.X:0}, {r.Y:0} · {r.Width:0}×{r.Height:0}"});}
    }
    private void RemoveRedaction(int index)
    {
        if(index<0||index>=redactions.Count)return;
        drawing.Children.Remove(redactionShapes[index]);redactionShapes.RemoveAt(index);redactions.RemoveAt(index);ListRedactions();
    }
    private RectD[] ReadRedactions()=>redactions.Count>0?redactions.ToArray():throw new ArgumentException("Draw at least one area to redact.");
    // Test seams for Smoke/ImageEditorChecks.cs.
    internal void DragRedactionForTest(Point from,Point to){StartRedaction(Clamp(from));ExtendRedaction(Clamp(to));FinishRedaction();}
    internal int RedactionCountForTest=>redactions.Count;
    internal void RemoveRedactionForTest(int index)=>RemoveRedaction(index);
    internal void SelectRedactionStyleForTest(string style)=>redactionStyle.SelectedItem=style;
}
