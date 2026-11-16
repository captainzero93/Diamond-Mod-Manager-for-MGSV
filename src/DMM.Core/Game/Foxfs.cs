// foxfs.dat (chunk0.dat) chunk list and WMV entry edits
using System.Text;
using DMM.Core.Hashing;
using DMM.Core.Qar;

namespace DMM.Core.Game;

// Line edits match SnakeBite ModManager.AddChunks, ModifyFoxfs and UpdateFoxfs. SnakeBite reads with File.ReadAllLines and
// writes with File.WriteAllLines, so the result is UTF-8 without BOM, CRLF after every line.
public static class Foxfs
{
    public const string FileName = "foxfs.dat";
    private const string SafilesEnd = "\t</safiles>";
    private const string RetailChunk0Line = "\t\t<chunk id=\"0\" label=\"cypr\" qar=\"chunk0.dat\" textures=\"texture0.dat\"/>";

    private static readonly string[] ChunkLines =
    [
        "\t\t<chunk id=\"0\" label=\"old\" qar=\"a_chunk7.dat\" textures=\"a_texture7.dat\"/>",
        "\t\t<chunk id=\"1\" label=\"cypr\" qar=\"chunk0.dat\" textures=\"texture0.dat\"/>",
        "\t\t<chunk id=\"2\" label=\"base\" qar=\"chunk1.dat\" textures=\"texture1.dat\"/>",
        "\t\t<chunk id=\"3\" label=\"afgh\" qar=\"chunk2.dat\" textures=\"texture2.dat\"/>",
        "\t\t<chunk id=\"4\" label=\"mtbs\" qar=\"chunk3.dat\" textures=\"texture3.dat\"/>",
        "\t\t<chunk id=\"5\" label=\"mafr\" qar=\"chunk4.dat\" textures=\"texture4.dat\"/>",
        "\t\t<chunk id=\"6\" label=\"mgo\" qar=\"chunk5_mgo0.dat\" textures=\"texture5_mgo0.dat\"/>",
        "\t\t<chunk id=\"7\" label=\"gzs\" qar=\"chunk6_gzs0.dat\" textures=\"texture6_gzs0.dat\"/>",
    ];

    public static ulong Hash => FoxPath.NameToHash(FileName);

    public static byte[] Read(QarArchive chunk0)
    {
        if (!chunk0.TryGetEntry(Hash, out QarEntryInfo entry))
            throw new InvalidDataException($"{chunk0.Path} has no foxfs.dat");
        return chunk0.ReadContent(entry);
    }

    /// <summary>SnakeBite's setup check: foxfs.dat mentions a_chunk7.dat once setup has run.</summary>
    public static bool IsPrepared(byte[] foxfs) => Encoding.UTF8.GetString(foxfs).Contains("a_chunk7.dat", StringComparison.Ordinal);

    public static List<string> ToLines(byte[] foxfs)
    {
        var lines = new List<string>();
        using var reader = new StreamReader(new MemoryStream(foxfs), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        while (reader.ReadLine() is { } line)
            lines.Add(line);
        return lines;
    }

    public static byte[] FromLines(IEnumerable<string> lines)
    {
        var text = new StringBuilder();
        foreach (string line in lines)
            text.Append(line).Append("\r\n");
        return new UTF8Encoding(false).GetBytes(text.ToString());
    }

    /// <summary>SnakeBite AddChunks: replaces the six retail chunk lines with eight that add a_chunk7, MGO and GZ.</summary>
    public static byte[] AddChunks(byte[] retailFoxfs)
    {
        List<string> lines = ToLines(retailFoxfs);
        int start = lines.IndexOf(RetailChunk0Line);
        if (start < 0 || start + 6 > lines.Count)
            throw new InvalidDataException("foxfs.dat does not have the retail chunk list SnakeBite setup expects");
        lines.RemoveRange(start, 6);
        lines.InsertRange(start, ChunkLines);
        return FromLines(lines);
    }

    public static string WmvLine(ulong hash) => "\t\t<file code=\"" + hash + "\"/>";

    /// <summary>SnakeBite UpdateFoxfs: custom WMV codes go at the end of the safiles list, in mod order.</summary>
    public static byte[] AddWmvs(byte[] baseFoxfs, IReadOnlyList<ulong> wmvHashes)
    {
        if (wmvHashes.Count == 0)
            return baseFoxfs;
        List<string> lines = ToLines(baseFoxfs);
        int index = lines.IndexOf(SafilesEnd);
        if (index < 0)
            throw new InvalidDataException("foxfs.dat has no </safiles> line");
        lines.InsertRange(index, wmvHashes.Select(WmvLine));
        return FromLines(lines);
    }

    /// <summary>Removes custom WMV lines SnakeBite added, to recover the base foxfs.dat from a SnakeBite install.</summary>
    public static byte[] RemoveWmvs(byte[] foxfs, IEnumerable<ulong> wmvHashes)
    {
        var remove = new HashSet<string>(wmvHashes.Select(WmvLine), StringComparer.Ordinal);
        if (remove.Count == 0)
            return foxfs;
        return FromLines(ToLines(foxfs).Where(line => !remove.Contains(line)));
    }
}
