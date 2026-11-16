// Fox Engine QAR path hashing and path form conversion
using System.Globalization;

namespace DMM.Core.Hashing;

// Hashing matches GzsTool.Core Utility/Hashing.cs; NameToHash matches SnakeBite Tools.NameToHash.
public static class FoxPath
{
    public const ulong MetaFlag = 0x4000000000000;
    public const ulong PathHashMask = 0x3FFFFFFFFFFFF;
    private const ulong Seed0 = 0x9ae16a3b2f90404f;

    // GzsTool Hashing.FileExtensions
    private static readonly string[] FileExtensions =
    [
        "1.ftexs", "1.nav2", "2.ftexs", "3.ftexs", "4.ftexs", "5.ftexs", "6.ftexs", "ag.evf", "aia", "aib", "aibc", "aig",
        "aigc", "aim", "aip", "ait", "atsh", "bnd", "bnk", "cc.evf", "clo", "csnav", "dat", "des", "dnav", "dnav2", "eng.lng",
        "ese", "evb", "evf", "fag", "fage", "fago", "fagp", "fagx", "fclo", "fcnp", "fcnpx", "fdes", "fdmg", "ffnt", "fmdl",
        "fmdlb", "fmtt", "fnt", "fova", "fox", "fox2", "fpk", "fpkd", "fpkl", "frdv", "fre.lng", "frig", "frt", "fsd", "fsm",
        "fsml", "fsop", "fstb", "ftex", "fv2", "fx.evf", "fxp", "gani", "geom", "ger.lng", "gpfp", "grxla", "grxoc", "gskl",
        "htre", "info", "ita.lng", "jpn.lng", "json", "lad", "ladb", "lani", "las", "lba", "lng", "lpsh", "lua", "mas", "mbl",
        "mog", "mtar", "mtl", "nav2", "nta", "obr", "obrb", "parts", "path", "pftxs", "ph", "phep", "phsd", "por.lng", "qar",
        "rbs", "rdb", "rdf", "rnav", "rus.lng", "sad", "sand", "sani", "sbp", "sd.evf", "sdf", "sim", "simep", "snav",
        "spa.lng", "spch", "sub", "subp", "tgt", "tre2", "txt", "uia", "uif", "uig", "uigb", "uil", "uilb", "utxl", "veh",
        "vfx", "vfxbin", "vfxdb", "vnav", "vo.evf", "vpc", "wem", "wmv", "xml",
    ];

    private static readonly Dictionary<string, ulong> ExtensionIds = FileExtensions.ToDictionary(e => e, HashExtension, StringComparer.Ordinal);
    private static readonly Dictionary<ulong, string> ExtensionsById = ExtensionIds.ToDictionary(e => e.Value, e => e.Key);

    /// <summary>"/Assets/a/b.fpk" or "Assets\a\b.fpk" to "Assets\a\b.fpk" (SnakeBite Tools.ToWinPath).</summary>
    public static string ToWinPath(string path) => path.Replace('/', '\\').TrimStart('\\');

    /// <summary>"Assets\a\b.fpk" to "/Assets/a/b.fpk" (SnakeBite Tools.ToQarPath).</summary>
    public static string ToQarPath(string path) => "/" + path.Replace('\\', '/').TrimStart('/');

    public static ulong HashExtension(string extension) => HashFileName(extension, false) & 0x1FFF;

    public static bool TryGetExtension(ulong extensionId, out string extension) => ExtensionsById.TryGetValue(extensionId, out extension!);

    /// <summary>GzsTool Hashing.HashFileName.</summary>
    public static ulong HashFileName(string text, bool removeExtension = true)
    {
        if (removeExtension)
        {
            int index = text.IndexOf('.');
            if (index != -1)
                text = text[..index];
        }

        bool metaFlag = false;
        const string assets = "/Assets/";
        if (text.StartsWith(assets, StringComparison.Ordinal))
        {
            text = text[assets.Length..];
            if (text.StartsWith("tpptest", StringComparison.Ordinal))
                metaFlag = true;
        }
        else
        {
            metaFlag = true;
        }

        text = text.TrimStart('/');

        Span<byte> seed1Bytes = stackalloc byte[8];
        seed1Bytes.Clear();
        for (int i = text.Length - 1, j = 0; i >= 0 && j < 8; i--, j++)
            seed1Bytes[j] = checked((byte)text[i]);
        ulong seed1 = BitConverter.ToUInt64(seed1Bytes);

        ulong masked = CityHash.CityHash64WithSeeds(AnsiText.GetBytes(text), Seed0, seed1) & PathHashMask;
        return metaFlag ? masked | MetaFlag : masked;
    }

    /// <summary>GzsTool Hashing.HashFileNameLegacy, used for FPK entry data keys.</summary>
    public static ulong HashFileNameLegacy(string text, bool removeExtension = true)
    {
        if (removeExtension)
        {
            int index = text.IndexOf('.');
            if (index != -1)
                text = text[..index];
        }

        ulong seed1 = text.Length > 0 ? (uint)(text[0] << 16) + (uint)text.Length : 0;
        return CityHash.CityHash64WithSeeds(AnsiText.GetBytes(text + "\0"), Seed0, seed1) & 0xFFFFFFFFFFFF;
    }

    /// <summary>GzsTool Hashing.HashFileNameWithExtension: path hash with the extension id in the top 13 bits.</summary>
    public static ulong HashFileNameWithExtension(string filePath)
    {
        filePath = filePath.Replace('\\', '/');
        int extensionIndex = filePath.IndexOf('.');
        string hashablePart = extensionIndex == -1 ? filePath : filePath[..extensionIndex];
        string extensionPart = extensionIndex == -1 ? "" : filePath[(extensionIndex + 1)..];

        ulong typeId = ExtensionIds.TryGetValue(extensionPart, out ulong id) ? id : 0;
        return (typeId << 51) | HashFileName(hashablePart);
    }

    /// <summary>
    /// SnakeBite Tools.NameToHash. Files in the archive root named by a hex hash ("/1a2b3c.fpk") keep that hash;
    /// everything else is hashed from its path.
    /// </summary>
    public static ulong NameToHash(string fileName)
    {
        string filePath = ToQarPath(fileName);
        if (!filePath[1..].Contains('/'))
        {
            string rootName = filePath.TrimStart('/');
            int dot = rootName.IndexOf('.');
            if (dot == -1)
                throw new ArgumentException($"QAR file name has no extension: {fileName}", nameof(fileName));
            string fileNoExt = rootName[..dot];
            string fileExt = rootName[(dot + 1)..];
            if (ulong.TryParse(fileNoExt, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong parsed))
                return (HashExtension(fileExt) << 51) | parsed;
        }
        return HashFileNameWithExtension(filePath);
    }
}
