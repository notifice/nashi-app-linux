using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;
[assembly: System.Runtime.Versioning.SupportedOSPlatform("linux")]
namespace Nashi.Linux;

// Port of Nashi.Windows/SingleInstance.cs. A named Mutex on Unix lives in a shared /tmp/.dotnet directory and is not scoped to the user
// session, so the primary is chosen by an exclusive lock file instead (released by the kernel when the process dies) and the
// arguments of later launches travel over a Unix domain socket in the same directory.
internal sealed class SingleInstance:IDisposable
{
    private readonly FileStream? lockFile;private readonly string socketPath,lockPath;private readonly Socket? listener;private readonly CancellationTokenSource lifetime=new();
    internal bool Primary{get;}
    internal string SocketPath=>socketPath;
    internal SingleInstance(string? testName=null)
    {
        var name=testName??"nashi.desktop.v1";
        if(name.Length is 0 or >64||name.Any(c=>!(char.IsAsciiLetterOrDigit(c)||c is '.' or '-' or '_')))throw new ArgumentException("Invalid instance name.",nameof(testName));
        var runtime=Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var directory=runtime is not null&&Directory.Exists(runtime)?runtime:"/tmp/nashi-"+Uid();
        Directory.CreateDirectory(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        lockPath=Path.Combine(directory,name+".lock");socketPath=Path.Combine(directory,name+".sock");
        try{lockFile=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
        catch(IOException){return;}
        Primary=true;
        File.Delete(socketPath);
        listener=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));File.SetUnixFileMode(socketPath,UnixFileMode.UserRead|UnixFileMode.UserWrite);listener.Listen(8);
    }
    private static string Uid()
    {
        try{foreach(var line in File.ReadLines("/proc/self/status"))if(line.StartsWith("Uid:"))return line["Uid:".Length..].Split('\t',' ',StringSplitOptions.RemoveEmptyEntries)[0];}catch(IOException){}
        return Environment.UserName;
    }
    // The socket is already listening, so a second launch that arrives before Start waits for the acknowledgement instead of failing.
    internal void Start(Action<string[]> receive)
    {
        if(!Primary)throw new InvalidOperationException("Only the primary instance can listen.");_ = Task.Run(async()=>
        {
            while(!lifetime.IsCancellationRequested)
            {
                try
                {
                    using var connection=await listener!.AcceptAsync(lifetime.Token);using var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    using var stream=new NetworkStream(connection);var prefix=new byte[4];await stream.ReadExactlyAsync(prefix,timeout.Token);var length=BinaryPrimitives.ReadInt32LittleEndian(prefix);if(length is <2 or >131072)continue;var data=new byte[length];await stream.ReadExactlyAsync(data,timeout.Token);var paths=JsonSerializer.Deserialize<string[]>(data);if(paths is null||paths.Length>256||paths.Any(p=>p is null||p.Length>16384))continue;receive(paths);await stream.WriteAsync(new byte[]{1},timeout.Token);
                }
                catch(OperationCanceledException){if(lifetime.IsCancellationRequested)break;}catch(ObjectDisposedException)when(lifetime.IsCancellationRequested){break;}catch(Exception error)when(error is IOException or JsonException or SocketException){if(!lifetime.IsCancellationRequested)await Task.Delay(100);}
            }
        });
    }
    internal async Task Notify(string[] paths)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(4));using var client=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);
        await client.ConnectAsync(new UnixDomainSocketEndPoint(socketPath),timeout.Token);using var stream=new NetworkStream(client);
        var data=JsonSerializer.SerializeToUtf8Bytes(paths.Take(256));if(data.Length>131072)throw new IOException("Choose fewer files to open.");var prefix=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(prefix,data.Length);await stream.WriteAsync(prefix,timeout.Token);await stream.WriteAsync(data,timeout.Token);var ack=new byte[1];await stream.ReadExactlyAsync(ack,timeout.Token);
    }
    public void Dispose()
    {
        lifetime.Cancel();listener?.Dispose();
        if(Primary){try{File.Delete(socketPath);File.Delete(lockPath);}catch(IOException){}}
        lockFile?.Dispose();lifetime.Dispose();
    }
}
