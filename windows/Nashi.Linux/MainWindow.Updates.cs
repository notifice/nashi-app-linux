using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Nashi.Linux.Platform;
namespace Nashi.Linux;

// Release notifications (P4-D). Linux never installs updates: it only tells the user a newer release exists and opens its page.
internal sealed partial class MainWindow
{
    private static readonly TimeSpan UpdateCheckInterval=TimeSpan.FromHours(24);
    private Border? updateBanner;private TextBlock? updateCaption;private AvailableUpdate? availableUpdate;
    internal bool UpdateBannerVisible=>updateBanner?.IsVisible==true;
    private string UpdateStampPath=>Path.Combine(library.DirectoryPath,"update-check.txt");
    partial void InitUpdates()
    {
        // Smoke runs must never touch the network.
        if(Environment.GetEnvironmentVariable("NASHI_SMOKE_DIRECTORY") is not null)return;
        Opened+=async(_,_)=>await AutomaticUpdateCheckAsync();
        var timer=new DispatcherTimer{Interval=TimeSpan.FromHours(1)};timer.Tick+=async(_,_)=>await AutomaticUpdateCheckAsync();Closed+=(_,_)=>timer.Stop();timer.Start();
    }
    // At most once per 24 h, and only while the "Check for updates automatically" preference is on.
    internal async Task AutomaticUpdateCheckAsync()
    {
        if(!preferences.Value.AutomaticUpdateChecks||Services.Updates is null||availableUpdate is not null)return;
        try{if(DateTime.TryParse(File.ReadAllText(UpdateStampPath),null,System.Globalization.DateTimeStyles.RoundtripKind,out var last)&&DateTime.UtcNow-last<UpdateCheckInterval)return;}catch(IOException){}catch(UnauthorizedAccessException){}
        var found=await Services.Updates.CheckAsync(CancellationToken.None);
        try{File.WriteAllText(UpdateStampPath,DateTime.UtcNow.ToString("o"));}catch(IOException){}catch(UnauthorizedAccessException){}
        if(found is not null)OfferUpdate(found);
    }
    // The Settings button: always asks, and always answers.
    internal async Task CheckUpdatesNowAsync()
    {
        if(Services.Updates is null)return;
        var found=await Services.Updates.CheckAsync(CancellationToken.None);
        var owner=(Window?)settingsWindow??this;
        if(found is null){if(Services.Dialogs is not null)await Services.Dialogs.InfoAsync(owner,L10n.Text("You are up to date. No newer release was found."),L10n.Text("Check for updates"));return;}
        OfferUpdate(found);
        if(Services.Dialogs is not null&&await Services.Dialogs.ConfirmAsync(owner,string.Format(L10n.Text("Nashi {0} is available. Open the release page?"),found.Version),L10n.Text("Update available")))Services.Shell?.OpenUrl(found.Url);
    }
    internal void OfferUpdate(AvailableUpdate update)
    {
        availableUpdate=update;
        if(updateBanner is null)
        {
            updateCaption=Text("",16);
            var open=Button("Open release page",()=>{if(availableUpdate is not null)Services.Shell?.OpenUrl(availableUpdate.Url);});
            var dismiss=Button("Not now",()=>updateBanner!.IsVisible=false);
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,VerticalAlignment=VerticalAlignment.Center};buttons.Children.Add(open);buttons.Children.Add(dismiss);
            var row=new DockPanel();DockPanel.SetDock(buttons,Dock.Right);row.Children.Add(buttons);
            var caption=new StackPanel{VerticalAlignment=VerticalAlignment.Center};caption.Children.Add(updateCaption);caption.Children.Add(Text("A newer Nashi release is available. You choose when to get it.",12,"Muted"));row.Children.Add(caption);
            updateBanner=Card(row);home.Children.Insert(Math.Min(2,home.Children.Count),updateBanner);
        }
        updateCaption!.Text=string.Format(L10n.Text("Nashi {0} is available"),update.Version);
        updateBanner.IsVisible=true;
        Services.Tray?.UpdateAvailable(update.Version);
    }
}
