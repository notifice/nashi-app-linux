using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.LogicalTree;
using Nashi.Core;
namespace Nashi.Linux;

public sealed class LanguageState:INotifyPropertyChanged
{
    public static LanguageState Shared{get;}=new();
    private string language="en";
    public string Language{get=>language;set{var next=AppLocalization.Normalize(value);if(next==language)return;language=next;PropertyChanged?.Invoke(this,new(nameof(Language)));}}
    public event PropertyChangedEventHandler? PropertyChanged;
}
internal static class L10n
{
    internal static string Language{get=>LanguageState.Shared.Language;set=>LanguageState.Shared.Language=value;}
    internal static string Text(string source)=>AppLocalization.Text(source,Language);
    internal static string Uppercase(string source)=>AppLocalization.Uppercase(Text(source),Language);
    // Attach skips what Bind already localised, like Windows' BindingOperations.IsDataBound check.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AvaloniaObject,HashSet<AvaloniaProperty>> Bound=[];
    private static bool IsBound(AvaloniaObject target,AvaloniaProperty property)=>Bound.TryGetValue(target,out var set)&&set.Contains(property);
    internal static void Bind(AvaloniaObject target,AvaloniaProperty property,string source)
    {
        Bound.GetOrCreateValue(target).Add(property);
        target.Bind(property,new Binding(nameof(LanguageState.Language)){Source=LanguageState.Shared,Converter=TextConverter.Shared,ConverterParameter=source,Mode=BindingMode.OneWay});
    }
    internal static TextBlock Label(TextBlock text){Bind(text,TextBlock.TextProperty,text.Text??"");return text;}
    internal static T Content<T>(T control)where T:ContentControl{if(control.Content is string source)Bind(control,ContentControl.ContentProperty,source);return control;}
    internal static void SetText(TextBlock text,string source)=>Bind(text,TextBlock.TextProperty,source);
    internal static void SetTip(Control control,string source)=>Bind(control,ToolTip.TipProperty,source);
    internal static void Attach(ILogical root)
    {
        if(root is Button button&&button.Content is string content&&!IsBound(button,ContentControl.ContentProperty))Bind(button,ContentControl.ContentProperty,content);
        if(root is Expander header&&header.Header is string heading&&!IsBound(header,Expander.HeaderProperty))Bind(header,Expander.HeaderProperty,heading);
        if(root is Control element&&ToolTip.GetTip(element) is string tip&&!IsBound(element,ToolTip.TipProperty))Bind(element,ToolTip.TipProperty,tip);
        foreach(var child in root.LogicalChildren)Attach(child);
    }
    internal static void Choices(ItemsControl control,IMultiValueConverter? converter=null)
    {
        control.ItemTemplate=new FuncDataTemplate<object>((_,_)=>
        {
            var text=new TextBlock();
            var binding=new MultiBinding{Converter=converter??ChoiceConverter.Shared};binding.Bindings.Add(new Binding("."));binding.Bindings.Add(new Binding(nameof(LanguageState.Language)){Source=LanguageState.Shared});
            text.Bind(TextBlock.TextProperty,binding);return text;
        });
    }
    private sealed class TextConverter:IValueConverter
    {
        internal static readonly TextConverter Shared=new();
        public object? Convert(object? value,Type type,object? parameter,CultureInfo culture)=>AppLocalization.Text((string)parameter!,(string)value!);
        public object? ConvertBack(object? value,Type type,object? parameter,CultureInfo culture)=>throw new NotSupportedException();
    }
    private sealed class ChoiceConverter:IMultiValueConverter
    {
        internal static readonly ChoiceConverter Shared=new();
        public object? Convert(IList<object?> values,Type type,object? parameter,CultureInfo culture)=>values.Count==2&&values[0] is string source&&values[1] is string language?AppLocalization.Text(source,language):values.Count>0?values[0]:null;
    }
}
