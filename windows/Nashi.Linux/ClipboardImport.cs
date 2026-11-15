using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Nashi.Core;
using Nashi.Engine;
using Nashi.Linux.Platform;
namespace Nashi.Linux;

internal sealed record ClipboardResult(string[] Paths,string? Link,string Label);
internal static class ClipboardImport
{
    // Walk ancestors: a TextBox's focused element can be its internal text presenter.
    internal static bool IsEditing(object? element)
    {
        for(var node=element as Visual;node is not null;node=node.GetVisualParent())
            if(node is TextBox or ComboBox or NumericUpDown)return true;
        return false;
    }
    internal static async Task<ClipboardResult> ReadAsync(Window owner,LibraryStore library)
    {
        if(Services.Clipboard is null)throw new InvalidOperationException("Your clipboard is empty.");
        return Read(await Services.Clipboard.ReadAsync(owner),library);
    }
    internal static ClipboardResult Read(TransferContent data,LibraryStore library)
    {
        if(data.Files.Count>0)
        {
            var files=data.Files.Where(p=>File.Exists(p)||Directory.Exists(p)).Select(Path.GetFullPath).Distinct().ToArray();if(files.Length==0)throw new InvalidOperationException("Those files are no longer available.");
            return new(files,null,"Pasted files");
        }
        if(data.ImagePng is {} png)
        {
            var image=ImageEngine.FromPng(png);
            if(image.Width<1||image.Height<1||(long)image.Width*image.Height>100_000_000)throw new InvalidOperationException("This image is too large to paste. Choose the image file instead.");
            var file=Destination(library,"png");File.WriteAllBytes(file,png);return new([file],null,"Pasted image");
        }
        var text=data.Text;
        if(string.IsNullOrWhiteSpace(text)&&data.Url is null&&!string.IsNullOrWhiteSpace(data.Html))
        {
            if(Encoding.UTF8.GetByteCount(data.Html)>16*1024*1024)throw new InvalidOperationException("This text is too large to paste. Choose a text file instead.");
            var page=Destination(library,"html");File.WriteAllText(page,data.Html,new UTF8Encoding(false));return new([page],null,"Pasted text");
        }
        if(string.IsNullOrWhiteSpace(text)&&data.Url is null)throw new InvalidOperationException("Copy a file, some text, an image or a public media link first.");
        if(data.Url is {} link)return new([],link,"Copied link");
        var trimmed=text!.Trim();
        if(Uri.TryCreate(trimmed,UriKind.Absolute,out var uri)&&uri.Scheme is "https" or "http")return new([],uri.AbsoluteUri,"Copied link");
        if(Encoding.UTF8.GetByteCount(text)>16*1024*1024)throw new InvalidOperationException("This text is too large to paste. Choose a text file instead.");
        var path=Destination(library,"txt");File.WriteAllText(path,text,new UTF8Encoding(false));return new([path],null,"Pasted text");
    }
    private static string Destination(LibraryStore library,string extension)
    {
        var folder=Path.Combine(library.DirectoryPath,"Imports");Directory.CreateDirectory(folder);
        return Path.Combine(folder,$"Clipboard {DateTime.Now:yyyy-MM-dd HH-mm-ss}-{Guid.NewGuid().ToString("N")[..6]}.{extension}");
    }
    internal static bool IsClipboardError(Exception e)=>e is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or InvalidDataException or ArgumentException or ImageMagick.MagickException;
}
