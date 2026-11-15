using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Nashi.Core;
using Nashi.Engine;
using Nashi.Linux.Platform;
namespace Nashi.Linux;

// Port of Nashi.Windows/ToolSetupWindow.cs. Media tools download through Toolchain; ExifTool through MetadataService
// (needs system perl); Office comes from the distribution, so it only shows IOfficeRuntime.SetupHint unless the runtime can install.
internal sealed class ToolSetupWindow:Window
{
    private sealed record Tool(string Title,string Detail,Func<bool> Ready,bool CanInstall,string? Hint,string Button,Func<IProgress<OperationProgress>,CancellationToken,Task> Install,ToolGroup? Group);
    private readonly Tool[] tools;
    private readonly Action<bool> workingChanged;
    private readonly StackPanel groups=new();
    private readonly TextBlock status=Ui.Text("Download the missing tools to enable all conversions, previews and web downloads. Tools already found on your computer are kept.",13);
    private readonly ProgressBar progress=new(){Minimum=0,Maximum=1,Height=6,IsVisible=false,Margin=new(0,12,0,12)};
    private readonly Button next,cancel;
    private CancellationTokenSource? cancellation;
    private bool previouslyReady;
    internal bool Working{get;private set;}
    internal ToolSetupStatus Status=>new(tools[0].Ready(),tools[2].Ready());
    internal Button ContinueButton=>next;
    public ToolSetupWindow(Toolchain media,MetadataService metadata,IOfficeRuntime? office,Action<bool>? workingChanged=null)
    {
        this.workingChanged=workingChanged??(_=>{});
        var canInstallOffice=office?.CanInstall==true;
        tools=
        [
            new("Media tools","FFmpeg, ffprobe, yt-dlp and Deno enable media conversion, previews and web downloads. Approximately 180 MB. FFmpeg and yt-dlp use GPL licenses; Deno uses MIT.",()=>media.Ready,true,null,"Download media tools",(report,token)=>Task.Run(()=>media.InstallAsync(report,token),token),ToolGroup.Media),
            new("Metadata tool","ExifTool reads and edits photo, document and media metadata. It runs on your system Perl. Approximately 10 MB.",()=>metadata.Executable is not null,true,null,"Download metadata tool",(report,token)=>Task.Run(()=>metadata.InstallAsync(report,token),token),null),
            new("Office tools",canInstallOffice?"LibreOffice enables Office document conversions. MPL 2.0 and its included third-party licenses.":"LibreOffice enables Office document conversions. Install it from your distribution; Nashi uses the copy it finds on your computer.",()=>office?.Executable is not null,canInstallOffice,office?.SetupHint??"LibreOffice is not available in this build.","Download office tools",(report,token)=>Task.Run(()=>office!.InstallAsync(report,token),token),ToolGroup.Office)
        ];
        Nashi.Linux.Theme.Attach(this);Title="Set up Nashi tools";Width=646;Height=690;MinWidth=590;MinHeight=560;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel();Content=root;
        var footer=new StackPanel{Margin=new(24)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        footer.Children.Add(status);footer.Children.Add(progress);
        var actions=new DockPanel{Margin=new(0,14,0,0)};footer.Children.Add(actions);
        next=Ui.Primary("Download missing tools",async()=>{if(Pending().Length==0)Close();else await StartSetupAsync();});next.IsDefault=true;
        DockPanel.SetDock(next,Dock.Right);actions.Children.Add(next);
        cancel=Ui.Button("Cancel setup",()=>cancellation?.Cancel());actions.Children.Add(cancel);
        var content=new StackPanel{Margin=new(28)};root.Children.Add(new ScrollViewer{Content=content,VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled});
        content.Children.Add(Label("FIRST-TIME SETUP",11,"Gold"));content.Children.Add(Label("A few tools. More possibilities.",28));
        content.Children.Add(Label("One download per tool group. No account or GitHub token.",14));content.Children.Add(groups);
        var links=Ui.Button("Upstream versions, licenses and source links",()=>Services.Shell?.OpenUrl("https://github.com/orkunisitmak/nashi-app/blob/main/docs/dependencies.md"));links.Margin=new(0,8,0,0);content.Children.Add(links);
        Closing+=(_,e)=>{if(Working){e.Cancel=true;cancellation?.Cancel();}};
        Activated+=(_,_)=>{if(!Working)Refresh();};
        AddHandler(KeyDownEvent,(object? _,KeyEventArgs e)=>{if(e.Key==Key.Escape){if(Working)cancellation?.Cancel();else Close();e.Handled=true;}},RoutingStrategies.Tunnel);
        Refresh();
    }
    private static TextBlock Label(string text,double size=13,string? color=null){var label=Ui.Text(text,size,color);label.Margin=new(0,6,0,8);return label;}
    // Media and Office follow the Windows rules; a group only counts as pending when it can actually be installed here.
    private ToolGroup[] Pending()=>Status.Missing.Where(g=>tools.First(t=>t.Group==g).CanInstall).ToArray();
    private void Refresh()
    {
        var ready=Status;groups.Children.Clear();foreach(var tool in tools)Group(tool);
        var done=ready.Ready||Pending().Length==0;
        L10n.Bind(next,ContentControl.ContentProperty,done?"Continue to Nashi":"Download missing tools");next.IsEnabled=!Working;
        cancel.IsVisible=Working;progress.IsVisible=Working;
        if(ready.Ready&&!Working)L10n.SetText(status,"Ready. You can go back to your files.");
        else if(previouslyReady&&!Working)L10n.SetText(status,"Download the missing tools to enable all conversions, previews and web downloads. Tools already found on your computer are kept.");
        previouslyReady=ready.Ready;
    }
    private void Group(Tool tool)
    {
        var row=new StackPanel{Margin=new(0)};row.Children.Add(Label(tool.Title,18));row.Children.Add(Label(tool.Detail));
        if(tool.Ready())row.Children.Add(Label("Already available on your computer"));
        else if(tool.CanInstall){var download=Ui.Button(tool.Button,async()=>await StartSetupAsync(tool));download.IsEnabled=!Working;row.Children.Add(download);}
        else if(tool.Hint is not null)row.Children.Add(Label(tool.Hint,13,"Muted"));
        var card=Ui.Card(row);card.Margin=new(0,12,0,8);groups.Children.Add(card);
    }
    internal Task StartSetupAsync(ToolGroup? selected=null)=>StartSetupAsync(selected is null?null:tools.First(t=>t.Group==selected));
    private async Task StartSetupAsync(Tool? selected)
    {
        if(Working)return;
        var pending=(selected is null?tools.Where(t=>t.Group is not null&&Pending().Contains(t.Group.Value)):tools.Where(t=>t==selected&&!t.Ready()&&t.CanInstall)).ToArray();
        if(pending.Length==0){Refresh();return;}
        Working=true;cancellation=new();workingChanged(true);Refresh();progress.IsIndeterminate=true;
        var token=cancellation.Token;
        try
        {
            for(var i=0;i<pending.Length;i++)
            {
                token.ThrowIfCancellationRequested();var index=i;
                var report=new Progress<OperationProgress>(p=>{if(!Working||token.IsCancellationRequested)return;progress.IsIndeterminate=p.Fraction is null;if(p.Fraction is {} value)progress.Value=(index+value)/pending.Length;L10n.SetText(status,p.Label);});
                await pending[i].Install(report,token);Refresh();
            }
            if(pending.Any(t=>!t.Ready()))throw new InvalidDataException("The downloaded tools are incomplete. Please try setup again.");
            L10n.SetText(status,Status.Ready?"Ready. You can go back to your files.":"One tool group is ready. Download the remaining tools to finish setup.");
        }
        catch(OperationCanceledException){L10n.SetText(status,"Setup cancelled. Existing tools were kept.");}
        catch(Exception error){L10n.SetText(status,error.Message);}
        finally{Working=false;cancellation.Dispose();cancellation=null;workingChanged(false);Refresh();}
    }
}
