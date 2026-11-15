using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
namespace Nashi.Linux.Platform.Linux;

internal sealed class ScreenService:IScreenService
{
    private bool? reduced;
    public bool ReducedMotion=>reduced??=Detect();

    public void FitToWorkArea(Window window)
    {
        var screens=window.Screens;
        var screen=screens?.ScreenFromWindow(window)??screens?.Primary;
        if(screen is null)return;
        var area=screen.WorkingArea;var scale=screen.Scaling>0?screen.Scaling:1.0;
        if(!double.IsNaN(window.Width)&&window.Width>area.Width/scale)window.Width=area.Width/scale;
        if(!double.IsNaN(window.Height)&&window.Height>area.Height/scale)window.Height=area.Height/scale;
        var w=(int)Math.Round((double.IsNaN(window.Width)?window.Bounds.Width:window.Width)*scale);
        var h=(int)Math.Round((double.IsNaN(window.Height)?window.Bounds.Height:window.Height)*scale);
        var pos=window.Position;
        var x=Math.Max(area.X,Math.Min(pos.X,area.Right-w));
        var y=Math.Max(area.Y,Math.Min(pos.Y,area.Bottom-h));
        if(x!=pos.X||y!=pos.Y)window.Position=new PixelPoint(x,y);
    }

    // Best effort: GTK env override, then the GNOME setting. Cached for the process lifetime.
    private static bool Detect()
    {
        if(Environment.GetEnvironmentVariable("GTK_ENABLE_ANIMATIONS")=="0")return true;
        try
        {
            var psi=new ProcessStartInfo("gsettings"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
            foreach(var a in new[]{"get","org.gnome.desktop.interface","enable-animations"})psi.ArgumentList.Add(a);
            using var p=Process.Start(psi);
            if(p is null)return false;
            var o=p.StandardOutput.ReadToEnd().Trim();
            if(!p.WaitForExit(1500)){try{p.Kill();}catch{}return false;}
            return p.ExitCode==0&&o=="false";
        }
        catch{return false;}
    }
}
