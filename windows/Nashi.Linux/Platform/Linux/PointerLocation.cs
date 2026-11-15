using System.Globalization;
using Avalonia;
using Avalonia.Platform;
namespace Nashi.Linux.Platform.Linux;

/// A point on one screen in X (Avalonia) pixels, with that screen's working area and scaling, for placing a window centred on it.
internal readonly record struct ScreenSpot(Point Center,PixelRect WorkingArea,double Scaling)
{
    /// Top-left corner that centres a window of <paramref name="size"/> DIPs on the spot, kept inside the working area.
    internal PixelPoint Place(Size size)
    {
        var width=size.Width*Scaling;var height=size.Height*Scaling;var area=WorkingArea;
        static double Fit(double value,double low,double length,double room)=>room<=length?low+(room-length)/2:Math.Clamp(value,low,low+room-length);
        return new((int)Math.Round(Fit(Center.X-width/2,area.X,width,area.Width)),(int)Math.Round(Fit(Center.Y-height/2,area.Y,height,area.Height)));
    }
}
// The pointer for the global wheel shortcut. KWin knows it in its logical coordinates; XWayland's X coordinates are those times the largest output
// scale ("apps scale themselves") or the same ("scaled by the system"), so each KWin output is paired with an X screen and scaled by their width ratio.
internal static class PointerLocation
{
    internal readonly record struct Output(string Name,Rect Geometry);
    internal readonly record struct Report(Point Cursor,IReadOnlyList<Output> Outputs);
    private const string Script="var s=[];var l=workspace.screens||[];for(var i=0;i<l.length;i++){var g=l[i].geometry;s.push([l[i].name,g.x,g.y,g.width,g.height].join(','));}var c=workspace.cursorPos;report(c.x+','+c.y+(s.length?';'+s.join(';'):''));";
    /// Asks KWin for the pointer and its outputs; null outside KWin or when it does not answer in time.
    internal static async Task<Report?> QueryAsync(TimeSpan timeout)=>Parse(await KWinScript.RunAsync(Script,timeout));
    /// Parses "x,y;name,x,y,width,height;…" as the KWin script reports it.
    internal static Report? Parse(string? text)
    {
        if(string.IsNullOrWhiteSpace(text))return null;
        var parts=text.Split(';');var cursor=parts[0].Split(',');
        if(cursor.Length!=2||!Number(cursor[0],out var x)||!Number(cursor[1],out var y))return null;
        var outputs=new List<Output>();
        foreach(var part in parts.Skip(1))
        {
            var f=part.Split(',');if(f.Length!=5)continue;
            if(Number(f[1],out var ox)&&Number(f[2],out var oy)&&Number(f[3],out var w)&&Number(f[4],out var h)&&w>0&&h>0)outputs.Add(new(f[0],new(ox,oy,w,h)));
        }
        return new(new(x,y),outputs);
    }
    private static bool Number(string text,out double value)=>double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&double.IsFinite(value);
    /// Maps KWin's logical pointer to X pixels: the output under the pointer is paired with an X screen by name, else by position order when the counts agree,
    /// else with the only screen; without outputs (Plasma 5) the coordinates are taken as X pixels. Returns the point and the index of its screen, or null.
    internal static (Point Point,int Screen)? Map(Report report,IReadOnlyList<(string? Name,PixelRect Bounds)> screens)
    {
        if(screens.Count==0)return null;
        var cursor=report.Cursor;
        if(report.Outputs.Count==0)
        {
            for(var i=0;i<screens.Count;i++){var b=screens[i].Bounds;if(cursor.X>=b.X&&cursor.Y>=b.Y&&cursor.X<b.Right&&cursor.Y<b.Bottom)return(cursor,i);}
            return null;
        }
        var outputs=report.Outputs;
        static double Distance(Rect r,Point p){var dx=Math.Max(Math.Max(r.X-p.X,0),p.X-r.Right);var dy=Math.Max(Math.Max(r.Y-p.Y,0),p.Y-r.Bottom);return dx*dx+dy*dy;}
        var output=outputs.OrderBy(o=>Distance(o.Geometry,cursor)).First();
        var index=-1;
        for(var i=0;i<screens.Count&&index<0;i++)if(!string.IsNullOrEmpty(screens[i].Name)&&string.Equals(screens[i].Name,output.Name,StringComparison.OrdinalIgnoreCase))index=i;
        if(index<0&&outputs.Count==screens.Count)
        {
            var rank=outputs.OrderBy(o=>o.Geometry.X).ThenBy(o=>o.Geometry.Y).ToList().IndexOf(output);
            var ordered=Enumerable.Range(0,screens.Count).OrderBy(i=>screens[i].Bounds.X).ThenBy(i=>screens[i].Bounds.Y).ToList();index=ordered[rank];
        }
        if(index<0&&screens.Count==1)index=0;
        if(index<0)return null;
        var bounds=screens[index].Bounds;var g=output.Geometry;var scale=bounds.Width/g.Width;
        var x=Math.Clamp(bounds.X+(cursor.X-g.X)*scale,bounds.X,bounds.Right-1);var y=Math.Clamp(bounds.Y+(cursor.Y-g.Y)*scale,bounds.Y,bounds.Bottom-1);
        return(new(x,y),index);
    }
    /// Where the shortcut wheel goes: the pointer on its screen, else the centre of the primary screen (outside KWin, or when it does not answer).
    internal static ScreenSpot Spot(Report? report,IReadOnlyList<Screen> screens,Screen? primary)
    {
        if(report is {} r&&Map(r,screens.Select(s=>(s.DisplayName,s.Bounds)).ToList()) is var (point,index))return new(point,screens[index].WorkingArea,screens[index].Scaling);
        var fallback=primary??screens.FirstOrDefault();
        if(fallback is null)return new(new(0,0),new(0,0,1920,1080),1);
        var area=fallback.WorkingArea;return new(new(area.X+area.Width/2d,area.Y+area.Height/2d),area,fallback.Scaling);
    }
}
