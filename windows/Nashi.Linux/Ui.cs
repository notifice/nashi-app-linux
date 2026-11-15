using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
namespace Nashi.Linux;

// Shared factories that Nashi.Windows keeps as private statics on MainWindow (MainWindow.cs:108-115).
internal static class Ui
{
    internal static TextBlock Text(string value,double size,string? color=null,bool localize=true)
    {
        var text=new TextBlock{Text=value,FontSize=size,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,FontWeight=size>=17?FontWeight.SemiBold:FontWeight.Normal};if(color is not null)Theme.Res(text,TextBlock.ForegroundProperty,color);return localize?L10n.Label(text):text;
    }
    internal static Border Card(Control content){var card=new Border{CornerRadius=new(24),Padding=new(22),Margin=new(0,0,0,6),Child=content,BorderThickness=new(1)};Theme.Res(card,Border.BackgroundProperty,"Surface");Theme.Res(card,Border.BorderBrushProperty,"Line");return card;}
    internal static Button Button(string title,Action action){var button=L10n.Content(new Button{Content=title});button.Click+=(_,_)=>action();return button;}
    internal static Button Primary(string title,Action action){var button=Button(title,action);button.Classes.Add("primary");return button;}
}
