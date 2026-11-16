// Merges a mod pack into an existing pack like SnakeBite
namespace DMM.Core.Fpk;

public static class FpkMerge
{
    /// <summary>
    /// SnakeBite InstallManager.MergePacks with an existing source: the existing pack's files plus the mod pack's files
    /// (mod wins on the same name, compared case-insensitively as the Windows extraction folder does), ordered by
    /// SortFpksFiles, references taken from the existing pack.
    /// </summary>
    public static FpkArchive Merge(FpkArchive existing, FpkArchive mod, bool outputIsFpkd)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (FpkEntry entry in existing.Entries.Concat(mod.Entries))
            if (seen.Add(entry.Name))
                names.Add(entry.Name);

        var files = new Dictionary<string, FpkEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (FpkEntry entry in existing.Entries)
            files[entry.Name] = entry;
        foreach (FpkEntry entry in mod.Entries)
            files[entry.Name] = entry;

        var merged = new FpkArchive { IsFpkd = outputIsFpkd, References = new List<string>(existing.References) };
        foreach (string name in FpkLayout.SortFpkFiles(outputIsFpkd, names))
        {
            FpkEntry source = files[name];
            merged.Entries.Add(new FpkEntry { Name = name, Data = source.Data, Md5 = source.Md5, EncryptedName = source.EncryptedName });
        }
        return merged;
    }

    /// <summary>Names SortFpksFiles drops because their extension is not in the table for this pack type.</summary>
    public static IEnumerable<string> DroppedNames(FpkArchive existing, FpkArchive mod, bool outputIsFpkd)
    {
        var names = existing.Entries.Concat(mod.Entries).Select(e => e.Name).Distinct(StringComparer.Ordinal).ToList();
        if (names.Count <= 1)
            return [];
        var kept = new HashSet<string>(FpkLayout.SortFpkFiles(outputIsFpkd, names), StringComparer.Ordinal);
        return names.Where(n => !kept.Contains(n));
    }
}
