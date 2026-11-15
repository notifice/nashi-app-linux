using Avalonia.Controls;
using Avalonia.Threading;
using Nashi.Core;
using Nashi.Engine;
using Nashi.Linux.Platform;
namespace Nashi.Linux;

// Port of the wheel and tool flow of Nashi.Windows/MainWindow.cs (ShowWheel, ChooseFromWheel, EditAsync).
// Image tools run ToolEditor then ImageEngine.Apply; anything whose editor or engine is not ported yet says so instead of failing.
internal sealed partial class MainWindow
{
    private static readonly string[] FileActionIds=["float","copy","move","copyPath","terminal"];
    private static readonly string[] ImageToolIds=["resize","crop","rotate","flip","editImage","compress","createPDF"];
    partial void InitTools()=>Refresh();
    /// The wheel opens on the first mode that has something for the selection (Tools, then Convert, then File actions); Ctrl, Shift and Alt switch modes as set in Settings.
    private async void ShowWheel(){try{await ShowWheelAsync();}catch(Exception error){await ShowInfoAsync(error.Message,"Couldn’t open this tool");}}
    internal async Task<ToolChoice?> ShowWheelAsync(WheelMode? mode=null)
    {
        if(busy||files.Count==0)return null;
        var initial=mode??new[]{WheelMode.Tools,WheelMode.Convert,WheelMode.Actions}.FirstOrDefault(m=>WheelPresentation.Choices(files,m.Id()).Count>0);
        var choice=await WheelWindow.ShowAsync(this,files.ToArray(),initial);
        if(choice is not null)await RunToolAsync(choice);
        return choice;
    }
    private Task ShowUnavailableAsync(string title)=>ShowInfoAsync($"{L10n.Text(title)} isn’t available on Linux yet.","Not available yet");
    /// Runs one wheel choice: "convert:fmt" converts, "convert" opens the conversion wheel, image tools open ToolEditor and apply the result.
    internal async Task RunToolAsync(ToolChoice choice)
    {
        if(busy||files.Count==0)return;
        try
        {
            if(choice.Id=="convert"){await ShowWheelAsync(WheelMode.Convert);return;}
            if(await RunDocumentRouteAsync(choice))return;
            if(choice.Id.StartsWith("convert:",StringComparison.Ordinal)){await ConvertWithToastAsync(choice.Id[8..]);return;}
            if(FileActionIds.Contains(choice.Id)){await PerformFileAction(choice.Id,files.ToArray());return;}
            var id=choice.Id switch{"cropImage"=>"crop","annotateImage"=>"annotate","mergePDF"=>"mergePDFs",_=>choice.Id};
            if(!Images||!ImageToolIds.Contains(id)){await ShowUnavailableAsync(choice.Title);return;}
            var selected=files.ToArray();ToolEditorResult? accepted;
            try{accepted=await ToolEditor.ShowDetailedAsync(this,id,selected,preferences,title:choice.Title);}
            catch(NotSupportedException){await ShowUnavailableAsync(choice.Title);return;}
            if(accepted is null)return;
            var inputs=accepted.OrderedInputs is {Length:>0} ordered?ordered:selected;var destination=OutputDirectory(inputs);
            var options=accepted.Image with{EncoderPath=tools.Ready?tools.Executable("ffmpeg"):null};
            await RunWithToastAsync(choice.Title,(report,token)=>Task.Run<IReadOnlyList<string>>(()=>
            {
                using var tx=new ExportTransaction(destination);
                var exported=ImageEngine.Apply(id,inputs,tx.StagingDirectory,options,null,token,report);
                return tx.Publish(exported,token);
            },token));
        }
        catch(Exception error)when(error is not OperationCanceledException){await ShowInfoAsync(error.Message,"Couldn’t open this tool");}
    }
}
