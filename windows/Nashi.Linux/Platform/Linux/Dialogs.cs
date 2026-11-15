using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
namespace Nashi.Linux.Platform.Linux;

// Small themed modal windows. Brush keys (Paper/Ink/Surface/Line) come from App.Palette.
internal sealed class Dialogs:IDialogs
{
    public Task InfoAsync(Window? owner,string message,string title)=>ShowAsync(owner,message,title,false);
    public Task<bool> ConfirmAsync(Window? owner,string message,string title)=>ShowAsync(owner,message,title,true);

    private static Task<bool> ShowAsync(Window? owner,string message,string title,bool confirm)
    {
        var done=new TaskCompletionSource<bool>();
        var result=false;
        var window=new Window{Title=title,Width=440,SizeToContent=SizeToContent.Height,CanResize=false,ShowInTaskbar=owner is null,WindowStartupLocation=owner is null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner};
        window[!Window.BackgroundProperty]=new DynamicResourceExtension("Paper");
        var text=new TextBlock{Text=message,TextWrapping=Avalonia.Media.TextWrapping.Wrap};
        text[!TextBlock.ForegroundProperty]=new DynamicResourceExtension("Ink");
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Spacing=8};
        Button Make(string label,bool value,bool isDefault)
        {
            var b=new Button{Content=label,MinWidth=84,HorizontalContentAlignment=HorizontalAlignment.Center,IsDefault=isDefault,IsCancel=!isDefault,BorderThickness=new Thickness(1)};
            b[!TemplatedControl.BackgroundProperty]=new DynamicResourceExtension("Surface");
            b[!TemplatedControl.ForegroundProperty]=new DynamicResourceExtension("Ink");
            b[!TemplatedControl.BorderBrushProperty]=new DynamicResourceExtension("Line");
            b.Click+=(_,_)=>{result=value;window.Close();};
            return b;
        }
        buttons.Children.Add(Make("OK",true,true));
        if(confirm)buttons.Children.Add(Make("Cancel",false,false));
        window.Content=new StackPanel{Margin=new Thickness(24),Spacing=20,Children={text,buttons}};
        window.Closed+=(_,_)=>done.TrySetResult(result);
        if(owner is not null)_=window.ShowDialog(owner);else window.Show();
        return done.Task;
    }
}
