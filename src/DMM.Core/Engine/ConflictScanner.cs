// Conflicts between mods, worked out from their metadata.xml alone
using DMM.Core.Hashing;
using DMM.Core.Mgsv;

namespace DMM.Core.Engine;

public enum ConflictKind { DatFile, TextureFile, PackFile, GameDirFile }

/// <summary>Two or more mods supply the same file. ModIds are in load order; the last one's copy is used.</summary>
public sealed record Conflict(ConflictKind Kind, string Path, string? Pack, IReadOnlyList<string> ModIds)
{
    /// <summary>Path of the clash when a pack's file list is unknown: the whole pack is counted as shared.</summary>
    public const string UnlistedPack = "(some files, DMM couldn't check which)";

    public string Winner => ModIds[^1];
}

public sealed record ConflictReport(List<Conflict> Conflicts)
{
    public IEnumerable<Conflict> For(string modId) => Conflicts.Where(c => c.ModIds.Contains(modId));

    /// <summary>Files where this mod's copy is replaced by a mod later in the order.</summary>
    public int Overridden(string modId) => Conflicts.Count(c => c.ModIds.Contains(modId) && c.Winner != modId);
}

/// <summary>
/// Uses the same file routing as the install (ModFiles): whole QAR files in 00.dat and 01.dat, files inside packs (from
/// FpkEntries; packs themselves are merged, so only their inner files can clash), and game folder files. Needs no
/// extracted files, so it also previews mods that are not installed.
/// </summary>
public static class ConflictScanner
{
    /// <summary>
    /// packContents gives the files inside a pack a mod ships without listing them in FpkEntries (read from the mod's own copy);
    /// when it returns null the whole pack counts as shared with every other mod that ships it, since install merges them.
    /// </summary>
    public static ConflictReport Scan(IReadOnlyList<(string Id, ModEntry Metadata)> modsInOrder, QarDictionary dictionary,
        Func<string, ModQarFile, IEnumerable<string>?>? packContents = null)
    {
        var packOwners = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var unlisted = new HashSet<string>(StringComparer.Ordinal);
        var dat = new Dictionary<ulong, (string Path, List<string> Ids)>();
        var textures = new Dictionary<ulong, (string Path, List<string> Ids)>();
        var packs = new Dictionary<string, Dictionary<string, (string Name, List<string> Ids)>>(StringComparer.Ordinal);
        var gameDir = new Dictionary<string, (string Path, List<string> Ids)>(StringComparer.OrdinalIgnoreCase);

        foreach ((string id, ModEntry meta) in modsInOrder)
        {
            ModFileSet files = ModFiles.Read(meta, dictionary);
            foreach (ModQarFile file in files.Qar)
            {
                if (!file.IsPack || file.ToOne)
                {
                    Add(file.ToOne ? textures : dat, file.Hash, file.QarPath, id);
                    continue;
                }
                // packs are merged file by file at install, so only the files inside them can clash
                if (!packOwners.TryGetValue(file.QarPath, out List<string>? owners))
                    packOwners[file.QarPath] = owners = [];
                if (!owners.Contains(id))
                    owners.Add(id);
                if (files.PackFiles.ContainsKey(file.QarPath))
                    continue;
                if (packContents?.Invoke(id, file) is { } inner)
                    AddPackFiles(packs, file.QarPath, inner, id);
                else
                    unlisted.Add(file.QarPath);
            }
            foreach ((string pack, List<string> inner) in files.PackFiles)
            {
                if (!files.Qar.Any(q => q.IsPack && !q.ToOne && q.QarPath == pack))
                    continue;
                AddPackFiles(packs, pack, inner, id);
            }
            foreach (string path in files.GameDir.Distinct(StringComparer.OrdinalIgnoreCase))
                Add(gameDir, path, path, id);
        }

        var conflicts = new List<Conflict>();
        conflicts.AddRange(Collect(dat.Values, ConflictKind.DatFile, null));
        conflicts.AddRange(Collect(textures.Values, ConflictKind.TextureFile, null));
        foreach ((string pack, var files) in packs.OrderBy(p => p.Key, StringComparer.Ordinal))
            conflicts.AddRange(Collect(files.Values, ConflictKind.PackFile, pack));
        foreach (string pack in unlisted.Order(StringComparer.Ordinal))
            if (packOwners[pack].Count > 1)
                conflicts.Add(new Conflict(ConflictKind.PackFile, Conflict.UnlistedPack, pack, packOwners[pack]));
        conflicts.AddRange(Collect(gameDir.Values, ConflictKind.GameDirFile, null));
        return new ConflictReport(conflicts);
    }

    private static void AddPackFiles(Dictionary<string, Dictionary<string, (string Name, List<string> Ids)>> packs, string pack, IEnumerable<string> inner, string id)
    {
        if (!packs.TryGetValue(pack, out var packFiles))
            packs[pack] = packFiles = new Dictionary<string, (string, List<string>)>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in inner.Distinct(StringComparer.OrdinalIgnoreCase))
            Add(packFiles, name, name, id);
    }

    private static void Add<TKey>(Dictionary<TKey, (string Path, List<string> Ids)> map, TKey key, string path, string id) where TKey : notnull
    {
        if (!map.TryGetValue(key, out var entry))
            map[key] = entry = (path, []);
        if (entry.Ids.Count == 0 || entry.Ids[^1] != id)
            entry.Ids.Add(id);
    }

    private static IEnumerable<Conflict> Collect(IEnumerable<(string Path, List<string> Ids)> entries, ConflictKind kind, string? pack) =>
        entries.Where(e => e.Ids.Distinct().Count() > 1)
            .OrderBy(e => e.Path, StringComparer.Ordinal)
            .Select(e => new Conflict(kind, e.Path, pack, e.Ids));
}
