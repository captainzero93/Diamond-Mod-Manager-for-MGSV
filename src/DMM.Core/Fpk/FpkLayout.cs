// Archive extension rules and FPK file ordering from SnakeBite
namespace DMM.Core.Fpk;

// SnakeBite GzsLib.archiveExtensions, SortFpksFiles and IsExtensionValidForArchive
public static class FpkLayout
{
    private static readonly Dictionary<string, string[]> ArchiveExtensions = new(StringComparer.Ordinal)
    {
        ["dat"] =
        [
            "bnk", "dat", "ffnt", "fmtt", "fpk", "fpkd", "fsm", "fsop", "ftex", "ftexs", "json", "lua", "pftxs", "sbp", "subp", "wem",
        ],
        ["fpk"] =
        [
            "caar", "fnt", "atsh", "frig", "adm", "frt", "fpkl", "fsm", "ftdp", "geobv", "ftex", "geoms", "gimr", "gpfp", "grxla",
            "grxoc", "htre", "lba", "lpsh", "mog", "mtar", "nav2", "nta", "rdf", "ends", "sand", "mbl", "tcvp", "spch", "trap",
            "uigb", "uilb", "pcsp", "tre2", "fstb", "twpf", "fv2t", "fmdl", "geom", "gskl", "fcnp", "frdv", "fdes", "fclo", "uif",
            "uia", "subp", "sani", "ladb", "frl", "fv2", "obr", "lng2", "mtard", "obrb", "dfrm", "gani",
        ],
        ["fpkd"] =
        [
            "fox2", "evf", "parts", "vfxlb", "vfx", "vfxlf", "veh", "frld", "des", "bnd", "tgt", "phsd", "ph", "sim", "clo", "fsd",
            "sdf", "lua", "lng",
        ],
    };

    /// <summary>Last extension without the dot, as Path.GetExtension gives it.</summary>
    public static string LastExtension(string fileName)
    {
        int slash = fileName.LastIndexOfAny(['/', '\\']);
        int dot = fileName.LastIndexOf('.');
        return dot > slash ? fileName[(dot + 1)..] : "";
    }

    /// <summary>
    /// archiveName is "x.dat", "x.fpk" or "x.fpkd" (or just its extension). SnakeBite also lets ".dat" entries through
    /// when the path contains "wmv"; callers handle that.
    /// </summary>
    public static bool IsExtensionValidForArchive(string fileName, string archiveName)
    {
        string archiveExtension = LastExtension(archiveName);
        if (archiveExtension.Length == 0)
            archiveExtension = archiveName.TrimStart('.');
        return ArchiveExtensions.TryGetValue(archiveExtension, out string[]? valid) && valid.Contains(LastExtension(fileName), StringComparer.Ordinal);
    }

    /// <summary>
    /// SnakeBite SortFpksFiles: ordinal ascending for fpk, descending for fpkd, then grouped by the extension order table.
    /// Files whose extension is not in the table are dropped, except when there is only one file.
    /// </summary>
    public static List<string> SortFpkFiles(bool fpkd, List<string> fpkFiles)
    {
        if (fpkFiles.Count <= 1)
            return fpkFiles;

        var sorted = new List<string>(fpkFiles);
        if (fpkd)
            sorted.Sort((a, b) => string.CompareOrdinal(b, a));
        else
            sorted.Sort(StringComparer.Ordinal);

        var result = new List<string>(sorted.Count);
        foreach (string extension in ArchiveExtensions[fpkd ? "fpkd" : "fpk"])
            foreach (string fileName in sorted)
                if (LastExtension(fileName) == extension)
                    result.Add(fileName);
        return result;
    }
}
