using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nashi.Linux.Platform;
using Shapes=Avalonia.Controls.Shapes;
namespace Nashi.Linux;

/// A compact file stack that opens into a narrow, temporary shelf. Port of Nashi.Windows/FloatShelf.cs without edge snapping, Quick Preview and Share.
internal sealed class FloatShelf:Window
{
    private readonly ListBox filesBox=new(){SelectionMode=SelectionMode.Multiple,BorderThickness=new(0),Background=Brushes.Transparent,Padding=new(8,4,8,4)};
    private readonly List<string> paths=[];
    private readonly Func<IReadOnlyList<string>,string,Task> action;
    private readonly Action<IReadOnlyList<string>> reuse;
    private readonly Grid root=new(),header=new(){Height=136,Background=Brushes.Transparent};
    private readonly Border glass=new(){CornerRadius=new(24),BorderThickness=new(1)};
    private readonly Canvas stack=new(){Width=100,Height=100,Background=Brushes.Transparent,VerticalAlignment=VerticalAlignment.Top,Margin=new(0,8,0,0)};
    private readonly Button count=new(){Height=24,Width=104,FontSize=11,Padding=new(0),Background=Brushes.Transparent,BorderThickness=new(0),HorizontalContentAlignment=HorizontalAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,Margin=new(0,110,0,0)};
    private readonly Border countPlate=new(){Width=104,Height=24,CornerRadius=new(11),VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,110,0,0),IsHitTestVisible=false};
    private readonly TextBlock status=new(){FontSize=9,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    private readonly Button close,options;
    private readonly DispatcherTimer hover=new();
    private bool icons=true,interacting,dragging,hoverInside;
    private Point origin;
    private PointerPressedEventArgs? pressed;
    internal bool IsExpanded{get;private set;}
    internal IReadOnlyList<string> Paths=>paths;
    internal int SelectedCount=>filesBox.SelectedItems?.Count??0;

    public FloatShelf(IEnumerable<string> initial,Func<IReadOnlyList<string>,string,Task> action,Action<IReadOnlyList<string>> reuse)
    {
        this.action=action;this.reuse=reuse;Nashi.Linux.Theme.Attach(this);Title="Float · Nashi";
        WindowDecorations=WindowDecorations.None;TransparencyLevelHint=[WindowTransparencyLevel.Transparent];Background=Brushes.Transparent;CanResize=false;Topmost=true;ShowInTaskbar=false;
        Width=112;Height=136;MinWidth=112;MinHeight=136;WindowStartupLocation=WindowStartupLocation.CenterScreen;DragDrop.SetAllowDrop(this,true);
        root.RowDefinitions.Add(new(136,GridUnitType.Pixel));root.RowDefinitions.Add(new(1,GridUnitType.Star));root.RowDefinitions.Add(new(0,GridUnitType.Pixel));
        var shell=new Grid();shell.Children.Add(glass);shell.Children.Add(root);Content=shell;header.Children.Add(stack);header.Children.Add(countPlate);header.Children.Add(count);
        root.Children.Add(header);
        close=Control("×","Close Float without deleting files",Close);close.HorizontalAlignment=HorizontalAlignment.Left;close.Margin=new(6,6,0,0);header.Children.Add(close);
        options=Control("···","Float options",Options);options.HorizontalAlignment=HorizontalAlignment.Right;options.Margin=new(0,6,6,0);header.Children.Add(options);
        Grid.SetRow(filesBox,1);root.Children.Add(filesBox);Grid.SetRow(status,2);root.Children.Add(status);
        count.Click+=(_,_)=>SetExpanded(!IsExpanded);
        header.PointerPressed+=(_,e)=>{if(e.Source==header&&e.GetCurrentPoint(header).Properties.IsLeftButtonPressed){MoveShelf(e);e.Handled=true;}};
        stack.PointerPressed+=(_,e)=>
        {
            if(!e.GetCurrentPoint(stack).Properties.IsLeftButtonPressed)return;
            pressed=e;origin=e.GetPosition(stack);dragging=false;interacting=true;hover.Stop();e.Handled=true;
        };
        stack.PointerMoved+=(_,e)=>
        {
            if(pressed is null||!e.GetCurrentPoint(stack).Properties.IsLeftButtonPressed||Distance(e.GetPosition(stack),origin)<4)return;
            dragging=true;var trigger=pressed;pressed=null;
            // The wire clip is the move grip. The artwork carries the actual files.
            if(origin.X>62&&origin.Y<60)MoveShelf(trigger);else Carry(trigger,paths.ToArray());e.Handled=true;
        };
        stack.PointerReleased+=(_,e)=>{if(pressed is null)return;pressed=null;interacting=false;if(!dragging)SetExpanded(!IsExpanded);e.Handled=true;};
        filesBox.AddHandler(PointerPressedEvent,(_,e)=>{pressed=e;origin=e.GetPosition(filesBox);},Avalonia.Interactivity.RoutingStrategies.Tunnel,true);
        filesBox.AddHandler(PointerReleasedEvent,(_,_)=>pressed=null,Avalonia.Interactivity.RoutingStrategies.Tunnel,true);
        filesBox.PointerMoved+=(_,e)=>
        {
            if(pressed is null||!e.GetCurrentPoint(filesBox).Properties.IsLeftButtonPressed||Distance(e.GetPosition(filesBox),origin)<=8||SelectedCount==0)return;
            var trigger=pressed;pressed=null;Carry(trigger,Selection());
        };
        filesBox.SelectionChanged+=(_,_)=>UpdateLabels();
        filesBox.DoubleTapped+=(_,_)=>{foreach(var p in Selection())Open(p);};KeyDown+=OnKey;
        PointerEntered+=(_,_)=>ScheduleHover(true);PointerExited+=(_,_)=>ScheduleHover(false);
        hover.Tick+=(_,_)=>{hover.Stop();if(interacting)return;if(hoverInside&&IsPointerOver)SetExpanded(true);else if(!hoverInside&&!IsPointerOver)SetExpanded(false);};
        AddHandler(DragDrop.DragOverEvent,(_,e)=>{e.DragEffects=e.DataTransfer.Contains(DataFormat.File)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;});
        AddHandler(DragDrop.DropEvent,async(_,e)=>{e.Handled=true;try{var dropped=await Services.Drop.ReadAsync(e);Add(dropped.Files);}catch(Exception error)when(ActionError(error)){await Report(error);}});
        filesBox.ContextMenu=Menu();stack.ContextMenu=Menu();
        LanguageState.Shared.PropertyChanged+=LanguageChanged;Closed+=(_,_)=>{hover.Stop();LanguageState.Shared.PropertyChanged-=LanguageChanged;};
        Add(initial);Present();
    }
    private static double Distance(Point a,Point b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y));
    private void LanguageChanged(object? sender,PropertyChangedEventArgs e){Reload();filesBox.ContextMenu=Menu();stack.ContextMenu=Menu();}
    private Button Control(string text,string help,Action click)
    {
        var b=new Button{Content=text,Width=24,Height=24,Padding=new(0),FontSize=16,VerticalAlignment=VerticalAlignment.Top,HorizontalContentAlignment=HorizontalAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center,Background=Brushes.Transparent,BorderThickness=new(0)};
        L10n.SetTip(b,help);Avalonia.Automation.AutomationProperties.SetName(b,L10n.Text(help));b.Click+=(_,_)=>click();return b;
    }
    private void ScheduleHover(bool inside){hover.Stop();hoverInside=inside;hover.Interval=TimeSpan.FromMilliseconds(inside?200:650);hover.Start();}
    private void MoveShelf(PointerPressedEventArgs trigger){interacting=true;hover.Stop();try{BeginMoveDrag(trigger);}catch(InvalidOperationException){}finally{interacting=false;ScheduleHover(false);}}
    private async void Carry(PointerPressedEventArgs trigger,string[] selected)
    {
        if(selected.Length==0||Services.Drag is null){interacting=false;return;}
        interacting=true;hover.Stop();
        try{await Services.Drag.StartFilesAsync(this,trigger,selected);}
        catch(Exception error)when(ActionError(error)){await Report(error);}
        finally{interacting=false;Prune();ScheduleHover(false);}
    }
    internal void Add(IEnumerable<string> incoming)
    {
        foreach(var p in incoming.Where(p=>File.Exists(p)||Directory.Exists(p)).Take(1000)){var full=Path.GetFullPath(p);if(!paths.Contains(full,StringComparer.Ordinal))paths.Add(full);}
        Reload();
    }
    /// Forgets files that no longer exist, for example after a drop target moved them.
    internal void Prune(){if(paths.RemoveAll(p=>!File.Exists(p)&&!Directory.Exists(p))>0)Reload();}
    private string[] Selection()=>SelectedCount>0?filesBox.SelectedItems!.Cast<ListBoxItem>().Select(i=>(string)i.Tag!).ToArray():paths.Where(p=>File.Exists(p)||Directory.Exists(p)).ToArray();
    private static void Open(string path)=>Services.Shell.OpenUrl(new Uri(path).AbsoluteUri);
    private void Reload()
    {
        var selected=(filesBox.SelectedItems?.Cast<ListBoxItem>().Select(i=>(string)i.Tag!)??[]).ToHashSet(StringComparer.Ordinal);filesBox.Items.Clear();
        foreach(var path in paths)
        {
            var column=new StackPanel{Orientation=icons?Orientation.Vertical:Orientation.Horizontal};column.Children.Add(FloatArtwork.Image(path,icons?60:32));
            column.Children.Add(new TextBlock{Text=Path.GetFileName(path),TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,TextAlignment=icons?TextAlignment.Center:TextAlignment.Left,FontSize=11,MaxHeight=32,Width=icons?112:204,Margin=icons?new(0,6,0,0):new(8,0,0,0),VerticalAlignment=VerticalAlignment.Center});
            var item=new ListBoxItem{Content=column,Tag=path,Padding=new(4),Margin=new(0,2,0,2),HorizontalContentAlignment=HorizontalAlignment.Center};ToolTip.SetTip(item,path);
            filesBox.Items.Add(item);if(selected.Contains(path))filesBox.SelectedItems?.Add(item);
        }
        DrawStack();UpdateLabels();if(IsExpanded)SetExpanded(true,true);
    }
    private void DrawStack()
    {
        stack.Children.Clear();var top=paths.TakeLast(3).ToArray();
        for(var index=0;index<top.Length;index++)
        {
            var offset=top.Length-index-1;var image=FloatArtwork.Image(top[index],72);image.RenderTransformOrigin=new(.5,.5,RelativeUnit.Relative);image.RenderTransform=new RotateTransform(offset*-7);image.IsHitTestVisible=false;Canvas.SetLeft(image,14-offset*2);Canvas.SetTop(image,17+offset*2);stack.Children.Add(image);
        }
        if(top.Length==0)stack.Children.Add(new TextBlock{Text="＋",FontSize=44,Width=100,TextAlignment=TextAlignment.Center,Margin=new(0,20,0,0),IsHitTestVisible=false});
        const string wire="M76,71 L57,28 C47,6 70,-1 78,19 L94,57 C102,74 87,78 81,63 L66,28";
        foreach(var (color,width) in new[]{("#55000000",4.5),("#C2CAC5",2.6),("#CCFFFFFF",.7)})stack.Children.Add(new Shapes.Path{Data=Geometry.Parse(wire),Stroke=new SolidColorBrush(Color.Parse(color)),StrokeThickness=width,StrokeLineCap=PenLineCap.Round,IsHitTestVisible=false});
    }
    private void UpdateLabels()
    {
        count.Content=(IsExpanded?"⌃  ":"⌄  ")+L10n.Text(paths.Count==1?"1 item":$"{paths.Count} items");ToolTip.SetTip(count,L10n.Text(IsExpanded?"Collapse Float":"Expand Float"));
        status.Text=L10n.Text(SelectedCount>0?$"{SelectedCount} selected":"Enter · Open");Title=$"Float · {paths.Count} · Nashi";
    }
    private void Present()
    {
        glass.IsVisible=IsExpanded;glass.Background=new LinearGradientBrush{StartPoint=new(.5,0,RelativeUnit.Relative),EndPoint=new(.5,1,RelativeUnit.Relative),GradientStops=[new(Nashi.Linux.Theme.Dark?Color.FromArgb(248,42,49,34):Color.FromArgb(248,250,231,185),0),new(Nashi.Linux.Theme.Dark?Color.FromArgb(248,26,39,30):Color.FromArgb(248,227,235,211),1)]};
        Nashi.Linux.Theme.Res(glass,Border.BorderBrushProperty,"Line");
        if(IsExpanded)countPlate.Background=Brushes.Transparent;else Nashi.Linux.Theme.Res(countPlate,Border.BackgroundProperty,"Surface");
        root.RowDefinitions[0].Height=new(IsExpanded?140:136,GridUnitType.Pixel);header.Height=IsExpanded?140:136;root.RowDefinitions[2].Height=new(IsExpanded?22:0,GridUnitType.Pixel);
        filesBox.IsVisible=status.IsVisible=close.IsVisible=options.IsVisible=IsExpanded;UpdateLabels();
    }
    internal void SetExpanded(bool value,bool forceLayout=false)
    {
        if(IsExpanded==value&&!forceLayout)return;hover.Stop();var oldWidth=Bounds.Width>0?Bounds.Width:Width;
        IsExpanded=value;Present();
        var screen=Screens.ScreenFromWindow(this)??Screens.Primary;var scale=DesktopScaling;
        var width=value?(icons?156:284):112d;var height=value?162+Math.Max(1,Math.Min(paths.Count,5))*(icons?112:52):136d;
        if(screen is not null)
        {
            var area=screen.WorkingArea;height=Math.Min(height,area.Height/scale-16);
            var x=Math.Clamp(Position.X+(int)Math.Round((oldWidth-width)/2*scale),area.X,Math.Max(area.X,area.Right-(int)Math.Round(width*scale)));
            var y=Math.Clamp(Position.Y,area.Y,Math.Max(area.Y,area.Bottom-(int)Math.Round(height*scale)));
            Width=width;Height=height;Position=new PixelPoint(x,y);
        }
        else{Width=width;Height=height;}
        if(value)filesBox.Focus();
    }
    private void Options(){var menu=Menu();menu.Open(options);}
    private ContextMenu Menu()
    {
        var menu=new ContextMenu();menu.Opening+=(_,_)=>{interacting=true;hover.Stop();};menu.Closed+=(_,_)=>{interacting=false;ScheduleHover(false);};
        foreach(var (label,id) in new[]{("Open","open"),("Show in file manager","reveal"),("Copy","copy"),("Copy Path","copyPath"),("Terminal","terminal"),("Remove from Float","remove"),("Use in Nashi","reuse")})
        {
            var item=new MenuItem();L10n.Bind(item,MenuItem.HeaderProperty,label);
            item.Click+=async(_,_)=>{try{if(id=="open")foreach(var path in Selection())Open(path);else if(id=="reveal")foreach(var path in Selection())Services.Shell.Reveal(path);else if(id=="remove")Remove();else if(id=="reuse")reuse(Selection());else await action(Selection(),id);}catch(Exception error)when(ActionError(error)){await Report(error);}};
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        foreach(var iconMode in new[]{true,false}){var item=new MenuItem{ToggleType=MenuItemToggleType.CheckBox,IsChecked=icons==iconMode};L10n.Bind(item,MenuItem.HeaderProperty,iconMode?"Icons":"List");item.Click+=(_,_)=>{icons=iconMode;Reload();SetExpanded(false);SetExpanded(true);};menu.Items.Add(item);}
        var add=new MenuItem();L10n.Bind(add,MenuItem.HeaderProperty,"Add Files…");add.Click+=(_,_)=>Choose();menu.Items.Add(add);
        var clear=new MenuItem();L10n.Bind(clear,MenuItem.HeaderProperty,"Clear Float");clear.Click+=(_,_)=>{paths.Clear();Reload();};menu.Items.Add(clear);
        return menu;
    }
    private void Remove(){foreach(var p in (filesBox.SelectedItems?.Cast<ListBoxItem>().Select(i=>(string)i.Tag!)??[]).ToArray())paths.Remove(p);Reload();}
    private async void Choose()
    {
        try{var picked=await Services.Files.OpenFilesAsync(this,L10n.Text("Add files to Float"));if(picked.Count>0)Add(picked);}
        catch(Exception error)when(ActionError(error)){await Report(error);}
    }
    private static bool ActionError(Exception error)=>error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception;
    private Task Report(Exception error)=>Services.Dialogs.InfoAsync(this,L10n.Text(error.Message),L10n.Text("Float action unavailable"));
    private async void OnKey(object? sender,KeyEventArgs e)
    {
        try
        {
            var control=e.KeyModifiers.HasFlag(KeyModifiers.Control);
            if(control&&e.KeyModifiers.HasFlag(KeyModifiers.Shift)&&e.Key==Key.C){await action(Selection(),"copyPath");e.Handled=true;return;}
            if(e.Key==Key.Escape){if(SelectedCount>0)filesBox.SelectedItems?.Clear();else SetExpanded(false);e.Handled=true;}
            else if(e.Key==Key.Delete){Remove();e.Handled=true;}
            else if(e.Key==Key.Enter){foreach(var p in Selection())Open(p);e.Handled=true;}
            else if(control&&e.Key==Key.A){SetExpanded(true);filesBox.SelectAll();e.Handled=true;}
            else if(control&&e.Key==Key.C){await action(Selection(),"copy");e.Handled=true;}
            else if(control&&e.Key==Key.V){if(Services.Clipboard is not null)Add((await Services.Clipboard.ReadAsync(this)).Files);e.Handled=true;}
            else if(control&&e.Key==Key.R){foreach(var p in Selection())Services.Shell.Reveal(p);e.Handled=true;}
            else if(control&&e.Key==Key.O){foreach(var p in Selection())Open(p);e.Handled=true;}
            UpdateLabels();
        }
        catch(Exception error)when(ActionError(error)){await Report(error);}
    }
}

internal static class FloatArtwork
{
    private static readonly string[] Previewable=[".png",".jpg",".jpeg",".bmp",".gif",".webp"];
    /// A thumbnail for images (filled in off the UI thread) and a lettered card for everything else.
    internal static Control Image(string path,int size)
    {
        var folder=Directory.Exists(path);var holder=new Grid{Width=size,Height=size};
        var card=new Border{CornerRadius=new(folder?5:8),Background=folder?new SolidColorBrush(Color.FromRgb(73,177,214)):Nashi.Linux.Theme.Brush("Sand"),BorderBrush=Nashi.Linux.Theme.Brush("Line"),BorderThickness=new(1)};
        card.Child=new TextBlock{Text=folder?"▰":Path.GetExtension(path).TrimStart('.').ToUpperInvariant(),FontSize=Math.Max(9,size/6d),FontWeight=FontWeight.SemiBold,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new(3),Foreground=Nashi.Linux.Theme.Brush("Ink")};
        holder.Children.Add(card);
        if(!folder&&Previewable.Contains(Path.GetExtension(path).ToLowerInvariant())&&Services.Artwork is not null)Load(holder,card,path);
        return holder;
    }
    private static async void Load(Grid holder,Control card,string path)
    {
        try
        {
            var pixels=await Services.Artwork!.ThumbnailAsync(path,128,CancellationToken.None);
            if(pixels is null)return;
            holder.Children.Add(new Image{Source=Bitmaps.ToBitmap(pixels),Stretch=Stretch.Uniform});holder.Children.Remove(card);
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException){}
    }
}
