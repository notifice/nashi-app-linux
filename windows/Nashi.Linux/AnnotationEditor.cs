using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Nashi.Engine;
namespace Nashi.Linux;
// Port of the annotate part of Nashi.Windows/PhotoEditorOptions.cs (pen, arrow, rectangle and text; no marker, shapes, magnifier or select/move).
// Marks are canvas children above the picture in image pixels, so rendering the canvas at image size flattens them into Annotated.
internal sealed partial class ToolEditor
{
    private readonly ComboBox drawingTool=new(){ItemsSource=new[]{"Pen","Arrow","Rectangle","Text"},SelectedIndex=0};
    private Shape? annotationShape;private Point annotationOrigin;
    private void BuildAnnotationOptions()
    {
        if(tool!="annotate")return;
        OrderedInputs=[input];
        options.Children.Add(L10n.Label(new TextBlock{Text="Drag on the photo to draw. Press Ctrl+Z to undo the last mark.",TextWrapping=TextWrapping.Wrap,Margin=new(4,4,4,8)}));
        options.Children.Add(L10n.Label(new TextBlock{Text="Drawing tool",Margin=new(4,12,4,0)}));L10n.Choices(drawingTool);options.Children.Add(drawingTool);
        var width=bitmap?.PixelSize.Width??1000;
        TextField("Annotation text",L10n.Text("Note"));TextField("Ink color","#FF5200");Field("Stroke width",Math.Max(3,Math.Round(width/250d)));Field("Font size",Math.Max(24,Math.Round(width/40d)));
        options.Children.Add(Ui.Button("Undo last mark",UndoAnnotation));
        resetPhotoView=()=>{annotationShape=null;drawingTool.SelectedIndex=0;};
        drawing.AddHandler(PointerPressedEvent,(_,e)=>
        {
            if(!e.GetCurrentPoint(drawing).Properties.IsLeftButtonPressed)return;drawing.Focus();
            try{BeginAnnotation(Clamp(e.GetPosition(drawing)));}catch(ArgumentException error){ToolTip.SetTip(this,L10n.Text(error.Message));return;}
            e.Pointer.Capture(drawing);e.Handled=true;
        },RoutingStrategies.Bubble,true);
        drawing.AddHandler(PointerMovedEvent,(_,e)=>MoveAnnotation(Clamp(e.GetPosition(drawing))),RoutingStrategies.Bubble,true);
        drawing.AddHandler(PointerReleasedEvent,(_,_)=>annotationShape=null,RoutingStrategies.Bubble,true);
    }
    private IBrush Ink()=>Color.TryParse(fields["Ink color"].Text?.Trim(),out var color)?new SolidColorBrush(color):throw new ArgumentException("Enter a valid color for Ink color.");
    private void BeginAnnotation(Point at)
    {
        annotationOrigin=at;var ink=Ink();var thickness=Math.Clamp(Value("Stroke width",3),1,100);
        switch(drawingTool.SelectedItem as string)
        {
            case "Text":
                var text=fields["Annotation text"].Text;if(string.IsNullOrWhiteSpace(text))throw new ArgumentException("Enter the annotation text first.");
                var label=new TextBlock{Text=text,Foreground=ink,FontSize=Math.Clamp(Value("Font size",24),6,500),IsHitTestVisible=false};drawing.Children.Add(label);Canvas.SetLeft(label,at.X);Canvas.SetTop(label,at.Y);annotationShape=null;return;
            case "Rectangle":annotationShape=new Rectangle{Stroke=ink,StrokeThickness=thickness,IsHitTestVisible=false};Canvas.SetLeft(annotationShape,at.X);Canvas.SetTop(annotationShape,at.Y);break;
            default:annotationShape=new Polyline{Stroke=ink,StrokeThickness=thickness,StrokeLineCap=PenLineCap.Round,StrokeJoin=PenLineJoin.Round,IsHitTestVisible=false,Points=[at]};break;
        }
        drawing.Children.Add(annotationShape);
    }
    private void MoveAnnotation(Point point)
    {
        switch(annotationShape)
        {
            case Rectangle rectangle:
                Canvas.SetLeft(rectangle,Math.Min(annotationOrigin.X,point.X));Canvas.SetTop(rectangle,Math.Min(annotationOrigin.Y,point.Y));rectangle.Width=Math.Max(1,Math.Abs(point.X-annotationOrigin.X));rectangle.Height=Math.Max(1,Math.Abs(point.Y-annotationOrigin.Y));break;
            case Polyline line when drawingTool.SelectedItem as string=="Arrow":
                var angle=Math.Atan2(point.Y-annotationOrigin.Y,point.X-annotationOrigin.X);var size=Math.Max(12,line.StrokeThickness*4);
                line.Points=[annotationOrigin,point,new(point.X-size*Math.Cos(angle-.5),point.Y-size*Math.Sin(angle-.5)),point,new(point.X-size*Math.Cos(angle+.5),point.Y-size*Math.Sin(angle+.5))];break;
            case Polyline line:line.Points.Add(point);break;
        }
    }
    private void UndoAnnotation(){if(drawing.Children.Count>1)drawing.Children.RemoveAt(drawing.Children.Count-1);annotationShape=null;}
    // Renders the picture and marks at image pixel size and returns straight-alpha BGRA.
    private PixelImage RenderAnnotations()
    {
        var size=bitmap?.PixelSize??throw new ArgumentException("Choose an image to annotate.");
        using var target=new RenderTargetBitmap(size,new Vector(96,96));target.Render(drawing);
        using var stream=new MemoryStream();target.Save(stream,new PngBitmapEncoderOptions());
        return ImageEngine.FromPng(stream.ToArray());
    }
    // Test seams for Smoke/ImageEditorChecks.cs.
    internal void SelectDrawingToolForTest(string name)=>drawingTool.SelectedItem=name;
    internal void DrawAnnotationForTest(Point from,Point to,params Point[] through)
    {
        BeginAnnotation(Clamp(from));foreach(var p in through)MoveAnnotation(Clamp(p));MoveAnnotation(Clamp(to));annotationShape=null;
    }
    internal int AnnotationCountForTest=>drawing.Children.Count-1;
}
