// Windows-1252 text encoding used for Fox paths
using System.Text;

namespace DMM.Core.Hashing;

// GzsTool and SnakeBite run on .NET Framework, where Encoding.Default is the ANSI code page. Paths are ASCII in practice.
public static class AnsiText
{
    public static readonly Encoding Encoding = Create();

    private static Encoding Create()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    public static byte[] GetBytes(string text) => Encoding.GetBytes(text);

    public static string GetString(ReadOnlySpan<byte> bytes) => Encoding.GetString(bytes);
}
