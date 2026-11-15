using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nashi.Linux.Platform;
namespace Nashi.Linux;
public sealed class ToastWindow:Window
{
    private readonly TextBlock label=new(){FontSize=16,FontWeight=FontWeight.SemiBold,TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock detail=new(){Margin=new(0,4,0,8),TextWrapping=TextWrapping.Wrap};
    private readonly ProgressBar bar=new(){Height=5,Minimum=0,Maximum=1,IsIndeterminate=true};
    private readonly Button action=new(){Content="Cancel",MinWidth=85};private readonly Action? cancel;private bool complete;
    public ToastWindow(Window owner,Action? cancel)
    {
        Nashi.Linux.Theme.Attach(this);Nashi.Linux.Theme.Res(bar,Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty,"Gold");Nashi.Linux.Theme.Res(bar,Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty,"Inset");this.cancel=cancel;L10n.Content(action);
        WindowDecorations=WindowDecorations.None;CanResize=false;TransparencyLevelHint=[WindowTransparencyLevel.Transparent];Background=Brushes.Transparent;ShowInTaskbar=false;ShowActivated=false;Topmost=true;SizeToContent=SizeToContent.Height;Width=510;WindowStartupLocation=WindowStartupLocation.Manual;
        var content=new StackPanel();content.Children.Add(label);content.Children.Add(detail);content.Children.Add(bar);
        var grid=new Grid();grid.ColumnDefinitions.Add(new(1,GridUnitType.Star));grid.ColumnDefinitions.Add(new(GridLength.Auto));grid.Children.Add(content);Grid.SetColumn(action,1);grid.Children.Add(action);action.Margin=new(18,0,0,0);action.VerticalAlignment=VerticalAlignment.Center;
        var card=new Border{CornerRadius=new(22),BorderThickness=new(1),Padding=new(24),Child=grid};Nashi.Linux.Theme.Res(card,Border.BackgroundProperty,"Surface");Nashi.Linux.Theme.Res(card,Border.BorderBrushProperty,"Line");Content=card;
        Opened+=(_,_)=>
        {
            var scale=owner.DesktopScaling;var x=owner.Position.X+(int)((owner.Bounds.Width-Width)/2*scale);var y=owner.Position.Y+(int)((owner.Bounds.Height-Bounds.Height-30)*scale);
            Position=new(Math.Max(8,x),Math.Max(8,y));
        };
        action.Click+=(_,_)=>{if(!complete){this.cancel?.Invoke();action.IsEnabled=false;}};Update(new(null,"A fresh file is on its way…"));
    }
    public void Update(Nashi.Core.OperationProgress progress){L10n.SetText(label,progress.Label);bar.IsIndeterminate=progress.Fraction is null;if(progress.Fraction is {} fraction)bar.Value=Math.Clamp(fraction,0,1);L10n.SetText(detail,progress.Fraction is {} value?$"{value:P0}":"Working…");}
    public void Complete(IReadOnlyList<string> outputs)
    {
        complete=true;L10n.SetText(label,outputs.Count==0?"All set. You’re ready to go.":$"Nicely done. {outputs.Count} {(outputs.Count==1?"file is":"files are")} ready.");detail.Text=outputs.Count>0?Path.GetFileName(outputs[0]):"Ready.";bar.IsVisible=false;L10n.Bind(action,ContentControl.ContentProperty,"Show in Explorer");action.IsEnabled=true;
        // Replace the cancel handler by disabling its callback after completion.
        action.Click+=(_,_)=>{if(outputs.Count>0&&(File.Exists(outputs[0])||Directory.Exists(outputs[0])))Services.Shell?.Reveal(outputs[0]);};
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};timer.Tick+=(_,_)=>{timer.Stop();if(Nashi.Linux.Theme.Animations&&Services.Screen?.ReducedMotion!=true){Transitions=[new DoubleTransition{Property=OpacityProperty,Duration=TimeSpan.FromMilliseconds(280)}];Opacity=0;DispatcherTimer.RunOnce(Close,TimeSpan.FromMilliseconds(300));}else Close();};timer.Start();
    }
}
