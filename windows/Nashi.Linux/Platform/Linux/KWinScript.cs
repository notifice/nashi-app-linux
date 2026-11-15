using System.Collections.Concurrent;
using Tmds.DBus.Protocol;
namespace Nashi.Linux.Platform.Linux;

// Runs a one-shot KWin script that reports a string back over D-Bus. Under XWayland the app cannot see the pointer over Wayland windows, KWin can.
// Tmds.DBus.Protocol already ships with Avalonia.FreeDesktop, so no extra package and no qdbus/gdbus process: the script calls back the unique name of this connection.
internal static class KWinScript
{
    private const string ReportPath="/org/nashi/KWinReport",ReportInterface="org.nashi.KWinReport";
    private static readonly ConcurrentDictionary<string,TaskCompletionSource<string>> Pending=new();
    private static readonly SemaphoreSlim Gate=new(1,1);private static DBusConnection? bus;
    /// Runs <paramref name="body"/> in KWin, where report(value) hands one string back. Null when KWin is absent, the script fails or nothing arrives in time; the script is always unloaded.
    internal static async Task<string?> RunAsync(string body,TimeSpan timeout)
    {
        var started=DateTime.UtcNow;TimeSpan Left(){var left=timeout-(DateTime.UtcNow-started);return left>TimeSpan.Zero?left:TimeSpan.Zero;}
        var token=Guid.NewGuid().ToString("N");var plugin="nashi-"+token;var pending=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var directory=Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is {Length:>0} runtime&&Directory.Exists(runtime)?runtime:Path.GetTempPath();var file=Path.Combine(directory,plugin+".js");
        DBusConnection? connection=null;var loaded=false;
        try
        {
            connection=await ConnectAsync().WaitAsync(Left());if(connection is null)return null;
            Pending[token]=pending;
            await File.WriteAllTextAsync(file,$"function report(v){{callDBus(\"{connection.UniqueName}\",\"{ReportPath}\",\"{ReportInterface}\",\"Report\",\"{token}\",String(v));}}\ntry{{\n{body}\n}}catch(e){{report(\"error:\"+e);}}\n");
            var id=await connection.CallMethodAsync(Call(connection,"/Scripting","org.kde.kwin.Scripting","loadScript","ss",file,plugin),(m,_)=>m.GetBodyReader().ReadInt32()).WaitAsync(Left());
            if(id<0)return null;loaded=true;
            // Plasma 6 puts the script at /Scripting/Script<id>, Plasma 5 at /<id>.
            try{await connection.CallMethodAsync(Call(connection,"/Scripting/Script"+id,"org.kde.kwin.Script","run",null)).WaitAsync(Left());}
            catch(DBusErrorReplyException){await connection.CallMethodAsync(Call(connection,"/"+id,"org.kde.kwin.Script","run",null)).WaitAsync(Left());}
            var result=await pending.Task.WaitAsync(Left());
            return result.StartsWith("error:",StringComparison.Ordinal)?null:result;
        }
        // Best effort: any failure (no bus, no KWin, a refused script, a timeout) means the caller falls back to the primary screen.
        catch(Exception error)
        {
            Console.Error.WriteLine("Nashi: KWin script unavailable: "+error.Message);
            if(error is DBusConnectionException or DBusConnectionClosedException or ObjectDisposedException)Reset(connection);
            return null;
        }
        finally
        {
            Pending.TryRemove(token,out _);
            if(loaded&&connection is not null)try{await connection.CallMethodAsync(Call(connection,"/Scripting","org.kde.kwin.Scripting","unloadScript","s",plugin),(m,_)=>m.GetBodyReader().ReadBool()).WaitAsync(TimeSpan.FromMilliseconds(500));}catch(Exception){}
            try{File.Delete(file);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){}
        }
    }
    /// Raises this process's window titled <paramref name="title"/>: KWin's focus-stealing prevention may leave an XWayland window that the shortcut opened unfocused, a script may focus it.
    internal static async Task<bool> ActivateAsync(string title,TimeSpan timeout)
    {
        var caption=System.Text.Json.JsonSerializer.Serialize(title);
        var body=$"var w=workspace.windowList();var done=false;for(var i=0;i<w.length&&!done;i++){{if(w[i].pid=={Environment.ProcessId}&&w[i].caption=={caption}){{workspace.activeWindow=w[i];done=true;}}}}report(done?'ok':'none');";
        return await RunAsync(body,timeout)=="ok";
    }
    private static MessageBuffer Call(DBusConnection connection,string path,string @interface,string member,string? signature,params string[] arguments)
    {
        using var writer=connection.GetMessageWriter();writer.WriteMethodCallHeader("org.kde.KWin",path,@interface,member,signature,MessageFlags.None);
        foreach(var argument in arguments)writer.WriteString(argument);
        return writer.CreateMessage();
    }
    private static async Task<DBusConnection?> ConnectAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if(bus is not null)return bus;
            if(DBusAddress.Session is not {Length:>0} address)return null;
            var connection=new DBusConnection(address);
            try{await connection.ConnectAsync();}catch{connection.Dispose();throw;}
            connection.AddMethodHandler(new ReportHandler());bus=connection;return connection;
        }
        finally{Gate.Release();}
    }
    private static void Reset(DBusConnection? connection){if(connection is null)return;if(Interlocked.CompareExchange(ref bus,null,connection)==connection)connection.Dispose();}
    private sealed class ReportHandler:IPathMethodHandler
    {
        public string Path=>ReportPath;public bool HandlesChildPaths=>false;
        public ValueTask HandleMethodAsync(MethodContext context)
        {
            var request=context.Request;
            if(request.InterfaceAsString!=ReportInterface||request.MemberAsString!="Report"||request.SignatureAsString!="ss"){context.ReplyUnknownMethodError();return ValueTask.CompletedTask;}
            var reader=request.GetBodyReader();var token=reader.ReadString();var value=reader.ReadString();
            if(Pending.TryGetValue(token,out var pending))pending.TrySetResult(value);
            if(!context.NoReplyExpected){using var writer=context.CreateReplyWriter(null);context.Reply(writer.CreateMessage());}
            return ValueTask.CompletedTask;
        }
    }
}
