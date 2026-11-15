using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Nashi.Linux.Platform;
namespace Nashi.Linux;

// Port of Nashi.Windows/PdfOrganizer.cs: thumbnails of every page to reorder, rotate, duplicate and remove.
internal sealed class PdfOrganizer:Window
{
    private sealed class Page(int number,byte[] image){public int Number{get;}=number;public byte[] Image{get;}=image;public int Rotation{get;set;}}
    private readonly CheckBox uniformWidth=new(){Content="Make all pages the same width",Margin=new(8)};internal bool UniformWidth=>uniformWidth.IsChecked==true;
    private readonly List<Page> pages=[];private readonly ListBox gallery=new(){SelectionMode=SelectionMode.Multiple};
    internal int[] Pages=>pages.Select(p=>p.Number).ToArray();internal Dictionary<int,int> Rotations=>pages.GroupBy(p=>p.Number).ToDictionary(g=>g.Key,g=>g.First().Rotation);internal int[] PageRotations=>pages.Select(p=>p.Rotation).ToArray();
    /// The organizer currently open; the smoke tests drive it.
    internal static PdfOrganizer? Active{get;private set;}
    internal void AcceptForTest()=>Close(true);
    private PdfOrganizer()
    {
        Active=this;Closed+=(_,_)=>{if(Active==this)Active=null;};
        Nashi.Linux.Theme.Attach(this);Title="Organize PDF · Nashi";Width=1000;Height=760;MinWidth=700;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new(26)};Content=root;
        var heading=L10n.Label(new TextBlock{Text="Organize PDF",FontSize=28,FontWeight=FontWeight.Bold,Margin=new(0,0,0,18)});DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
        var footer=new DockPanel{Margin=new(0,16,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var save=Ui.Primary("Save organized copy ↗",()=>{if(pages.Count==0){_=Services.Dialogs.InfoAsync(this,L10n.Text("Keep at least one page."),"Nashi");return;}Close(true);});DockPanel.SetDock(save,Dock.Right);footer.Children.Add(save);
        var controls=new WrapPanel();controls.Children.Add(Ui.Button("Move left",()=>Move(-1)));controls.Children.Add(Ui.Button("Move right",()=>Move(1)));controls.Children.Add(Ui.Button("Rotate",Rotate));controls.Children.Add(Ui.Button("Duplicate",Duplicate));controls.Children.Add(Ui.Button("Remove",Remove));controls.Children.Add(Ui.Button("Cancel",()=>Close(false)));controls.Children.Add(uniformWidth);footer.Children.Add(controls);
        gallery.ItemsPanel=new Avalonia.Controls.Templates.FuncTemplate<Panel?>(()=>new WrapPanel());root.Children.Add(gallery);
        AddHandler(KeyDownEvent,(object? _,KeyEventArgs e)=>{if(e.Key==Key.Escape){Close(false);e.Handled=true;}else if(e.Key==Key.Delete){Remove();e.Handled=true;}},Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }
    internal static async Task<PdfOrganizer> CreateAsync(string file)
    {
        var result=new PdfOrganizer();
        var renderer=Services.Pdf??throw new InvalidOperationException("PDF rendering isn’t available.");
        var images=await renderer.RenderAsync(file,72);for(var i=0;i<images.Length;i++)result.pages.Add(new(i+1,images[i]));result.Reload();return result;
    }
    private void Reload()
    {
        var selected=Selected();gallery.Items.Clear();
        foreach(var page in pages)
        {
            var content=new StackPanel();
            using(var stream=new MemoryStream(page.Image))
            {
                var picture=new Image{Source=new Bitmap(stream),Width=130,Height=170,Stretch=Stretch.Uniform};
                content.Children.Add(new LayoutTransformControl{LayoutTransform=new RotateTransform(page.Rotation),Child=picture});
            }
            content.Children.Add(L10n.Label(new TextBlock{Text="Page "+page.Number,TextAlignment=TextAlignment.Center,Margin=new(0,8,0,0)}));
            gallery.Items.Add(new ListBoxItem{Content=content,Tag=page,Margin=new(6),Padding=new(10),IsSelected=selected.Contains(page)});
        }
    }
    private Page[] Selected()=>gallery.SelectedItems?.Cast<ListBoxItem>().Select(i=>(Page)i.Tag!).ToArray()??[];
    private void Move(int delta){var selected=Selected();foreach(var item in delta<0?selected:selected.Reverse()){var index=pages.IndexOf(item);var next=index+delta;if(next<0||next>=pages.Count)continue;pages.RemoveAt(index);pages.Insert(next,item);}Reload();}
    private void Rotate(){foreach(var page in Selected())page.Rotation=(page.Rotation+90)%360;Reload();}
    private void Duplicate(){foreach(var page in Selected().Reverse()){var index=pages.IndexOf(page);pages.Insert(index+1,new(page.Number,page.Image){Rotation=page.Rotation});}Reload();}
    private void Remove(){if(pages.Count-Selected().Length<1)return;foreach(var page in Selected())pages.Remove(page);Reload();}

    // Test seams for Smoke/DocumentChecks.cs.
    internal void SelectForTest(int index){gallery.SelectedItems?.Clear();((ListBoxItem)gallery.Items[index]!).IsSelected=true;}
    internal void MoveForTest(int delta)=>Move(delta);
    internal void RotateForTest()=>Rotate();
    internal void DuplicateForTest()=>Duplicate();
    internal void RemoveForTest()=>Remove();
}
