using Avalonia.Controls;
namespace Nashi.Linux;
internal sealed partial class ToolEditor
{
    // The preset that last filled the size fields; editing the fields by hand drops it.
    private (double? Scale,int? LongestEdge,string Width,string Height)? compressionPreset;
    private (double? Scale,int? LongestEdge)? CompressionPreset()=>compressionPreset is {} p&&fields["Width"].Text==p.Width&&fields["Height"].Text==p.Height?(p.Scale,p.LongestEdge):null;
    private void BuildCompressionOptions()
    {
        if(tool!="compress")return;var preset=new ComboBox{ItemsSource=new[]{"Balanced","Strong"},SelectedIndex=0};L10n.Choices(preset);options.Children.Add(L10n.Label(new TextBlock{Text="Compression preset",Margin=new(4,12,4,0)}));options.Children.Add(preset);preset.SelectionChanged+=(_,_)=>fields["Quality (%)"].Text=preset.SelectedIndex==0?"80":"55";
        resetCompression=()=>preset.SelectedIndex=0;Field("Target size (KB; 0 = auto)",0);Field("Reduction (%; 0 = auto)",0);
        if(bitmap is not null)
        {
            var pixelWidth=bitmap.PixelSize.Width;var pixelHeight=bitmap.PixelSize.Height;
            Field("Width",pixelWidth);Field("Height",pixelHeight);var size=new ComboBox{ItemsSource=new[]{"Original dimensions","75%","50%","25%","2560 px longest edge","1920 px longest edge","1280 px longest edge","Custom dimensions"},SelectedIndex=0};L10n.Choices(size);options.Children.Add(L10n.Label(new TextBlock{Text="Image size",Margin=new(4,12,4,0)}));options.Children.Add(size);
            size.SelectionChanged+=(_,_)=>
            {
                compressionPreset=null;if(size.SelectedIndex==7)return;var longest=Math.Max(pixelWidth,pixelHeight);
                var factor=size.SelectedIndex switch{1=>.75,2=>.5,3=>.25,4=>Math.Min(1,2560d/longest),5=>Math.Min(1,1920d/longest),6=>Math.Min(1,1280d/longest),_=>1};
                fields["Width"].Text=Number(Math.Max(1,Math.Round(pixelWidth*factor)));fields["Height"].Text=Number(Math.Max(1,Math.Round(pixelHeight*factor)));
                if(size.SelectedIndex is >=1 and <=6)compressionPreset=(size.SelectedIndex<=3?factor:null,size.SelectedIndex>=4?new[]{2560,1920,1280}[size.SelectedIndex-4]:null,fields["Width"].Text!,fields["Height"].Text!);
            };
            resetCompression+=()=>size.SelectedIndex=0;
        }
    }
}
