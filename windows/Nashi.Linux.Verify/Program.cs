using System.Text.Json;
namespace Nashi.Linux.Verify;

internal static partial class Program
{
    private static int passed;
    internal static void Check(bool condition,string name){if(!condition)throw new InvalidOperationException("Check failed: "+name);passed++;}
    // Each check file adds itself by implementing one of these hooks.
    static partial void ImageChecks();
    static partial void CompositionChecks();
    static partial void PdfChecks();
    static partial void DocumentChecks();
    static partial void MoveChecks();
    private static int Main()
    {
        ImageChecks();CompositionChecks();PdfChecks();DocumentChecks();MoveChecks();
        Console.WriteLine(JsonSerializer.Serialize(new{passed,scope="Linux engine (Nashi.Engine)"}));
        return 0;
    }
}
