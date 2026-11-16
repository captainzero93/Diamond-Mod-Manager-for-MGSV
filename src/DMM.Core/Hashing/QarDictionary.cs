// qar_dictionary.txt lookup for hashed QAR file names
namespace DMM.Core.Hashing;

public sealed class QarDictionary
{
    private readonly Dictionary<ulong, string> pathsByHash = new();

    public int Count => pathsByHash.Count;

    public static QarDictionary Load(string path)
    {
        using var reader = new StreamReader(path);
        return Load(reader);
    }

    public static QarDictionary Load(TextReader reader)
    {
        var dictionary = new QarDictionary();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
                continue;
            ulong hash = FoxPath.HashFileName(line) & FoxPath.PathHashMask;
            dictionary.pathsByHash.TryAdd(hash, line);
        }
        return dictionary;
    }

    public bool TryGetPath(ulong hash, out string path) => pathsByHash.TryGetValue(hash & FoxPath.PathHashMask, out path!);

    /// <summary>
    /// File name GzsTool gives a QAR entry when extracting (Hashing.TryGetFileNameFromHash, then NormalizeFilePath):
    /// "Assets\tpp\x.fpk", "1a2b3c.fpk" when the path is unknown, "..._unknown" when the extension is unknown.
    /// </summary>
    public string GzsName(ulong hash)
    {
        string path = TryGetPath(hash, out string found) ? found : (hash & FoxPath.PathHashMask).ToString("x");
        string name = FoxPath.TryGetExtension(hash >> 51, out string extension) ? path + "." + extension : path + "_unknown";
        return FoxPath.ToWinPath(name);
    }

    /// <summary>
    /// SnakeBite HashingExtended.UpdateName: "/1a2b3c.fpk" becomes "/Assets/.../name.fpk" when the hex name is in the
    /// dictionary. Returns null when the name is not a hex hash or is not found.
    /// </summary>
    public string? UpdateName(string inputFile)
    {
        int slash = inputFile.LastIndexOfAny(['/', '\\']);
        string name = inputFile[(slash + 1)..];
        int lastDot = name.LastIndexOf('.');
        string ext = lastDot == -1 ? "" : name[lastDot..];
        string fileName = lastDot == -1 ? name : name[..lastDot];
        string extInner = "";
        int innerDot = fileName.LastIndexOf('.');
        if (innerDot != -1)
        {
            extInner = fileName[innerDot..];
            fileName = fileName[..innerDot];
        }

        if (!TryParseHex(fileName, out ulong hash))
            return null;
        return TryGetPath(hash, out string found) ? found + extInner + ext : null;
    }

    // Convert.ToUInt64(s, 16): optional 0x prefix, up to 16 hex digits
    private static bool TryParseHex(string text, out ulong value)
    {
        value = 0;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];
        if (text.Length == 0 || text.Length > 16)
            return false;
        foreach (char c in text)
        {
            int digit = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' => c - 'a' + 10,
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => -1,
            };
            if (digit < 0)
                return false;
            value = (value << 4) | (uint)digit;
        }
        return true;
    }
}
