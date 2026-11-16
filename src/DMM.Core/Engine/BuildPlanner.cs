// Plans archives, foxfs and game folder files for mods
using DMM.Core.Fpk;
using DMM.Core.Game;
using DMM.Core.Hashing;
using DMM.Core.Logging;
using DMM.Core.Mgsv;
using DMM.Core.Qar;

namespace DMM.Core.Engine;

/// <summary>Archives and data the planner reads. The caller owns and disposes the archives.</summary>
public sealed class PlanInputs
{
    public required GameLayout Layout { get; init; }
    public required QarArchive BaselineZero { get; init; }
    public required QarArchive BaselineOne { get; init; }
    public required byte[] BaseFoxfs { get; init; }
    /// <summary>Opened base archives in SnakeBite ReadBaseData priority order (missing ones left out).</summary>
    public required IReadOnlyList<QarArchive> BaseArchives { get; init; }
    /// <summary>The current 00.dat/01.dat when DMM wrote them: entries with matching content are copied unchanged.</summary>
    public QarArchive? CurrentZero { get; init; }
    public QarArchive? CurrentOne { get; init; }
    /// <summary>Pack records of the last apply, used with CurrentZero to skip merges whose inputs did not change.</summary>
    public IReadOnlyDictionary<string, PackRecord> PreviousPacks { get; init; } = new Dictionary<string, PackRecord>();
    public required Lazy<QarDictionary> QarDictionary { get; init; }
    public required Lazy<FpkDictionary> FpkDictionary { get; init; }
}

/// <summary>
/// Gives the same result as installing the mods one at a time, in order, with SnakeBite into a freshly prepared game
/// (InstallManager.InstallMods / AddToSettingsFpk / MergePacks / InstallLooseFtexs / InstallGameDirFiles, ModManager.UpdateFoxfs):
/// - QAR files go to 00.dat, except paths containing ".ftex" which go to 01.dat. Later mods replace earlier ones.
/// - A .fpk/.fpkd already placed by an earlier mod is merged with that mod's version.
/// - Otherwise, when the mod lists FpkEntries for the pack and a base archive has it, it is merged with the vanilla pack.
/// - Otherwise the mod's pack is used as it is.
/// - Game folder files are copied, later mods replacing earlier ones. Custom WMV codes are added to foxfs.dat.
/// Each file is worked out from only the mods that supply it, so a change to one mod redoes only that mod's files.
/// </summary>
public static class BuildPlanner
{
    private abstract record Content(string Name);
    private sealed record ModFileContent(string Name, LibraryMod Mod, string ZipName, UInt128 Md5) : Content(Name);
    private sealed record MergedContent(string Name, byte[] Data) : Content(Name);
    private sealed record ReusedContent(string Name, QarArchive Archive, QarEntryInfo Entry) : Content(Name);

    private sealed record PackStep(LibraryMod Mod, string ZipName, bool ListsFpkEntries);

    public static BuildPlan Plan(PlanInputs inputs, IReadOnlyList<LibraryMod> mods)
    {
        var warnings = new List<string>();
        var zero = new Dictionary<ulong, Content>();
        var one = new Dictionary<ulong, Content>();
        var packSteps = new Dictionary<string, List<PackStep>>(StringComparer.Ordinal);
        var gameDir = new Dictionary<string, GameDirFile>(StringComparer.OrdinalIgnoreCase);
        var wmv = new List<ulong>();

        foreach (LibraryMod mod in mods)
        {
            ModFileSet files = ModFiles.Read(mod.Metadata, inputs.QarDictionary.Value);
            warnings.AddRange(files.Warnings);
            foreach (ModQarFile file in files.Qar)
            {
                string zipName = ModFiles.ZipName(file, mod.HasFile);
                if (file.IsPack && !file.ToOne)
                {
                    if (!packSteps.TryGetValue(file.QarPath, out List<PackStep>? steps))
                        packSteps[file.QarPath] = steps = [];
                    steps.Add(new PackStep(mod, zipName, files.PackFiles.ContainsKey(file.QarPath)));
                    continue;
                }
                (file.ToOne ? one : zero)[file.Hash] = new ModFileContent(file.QarPath, mod, zipName, mod.FileMd5(zipName));
            }
            foreach (string relative in files.GameDir)
            {
                if (inputs.Layout.GameDirTarget(relative) == null)
                    continue;
                string zipName = MgsvPackage.GameDirFileName(relative);
                gameDir[relative] = new GameDirFile(relative, mod.Id, mod, zipName, Md5Key.ToHex(mod.FileMd5(zipName)));
            }
            foreach (ModWmvEntry entry in mod.Metadata.ModWmvEntries)
                if (!wmv.Contains(entry.Hash))
                    wmv.Add(entry.Hash);
        }

        // Pack paths are independent of each other, so they resolve in parallel; steps within a pack keep load order.
        var packResults = new Dictionary<string, (Content Content, PackRecord Record, bool Reused, int Merges)>(StringComparer.Ordinal);
        var packWarnings = new List<string>();
        var gate = new object();
        Parallel.ForEach(packSteps, pair =>
        {
            var localWarnings = new List<string>();
            var result = ResolvePack(pair.Key, pair.Value, inputs, localWarnings);
            lock (gate)
            {
                packResults[pair.Key] = result;
                packWarnings.AddRange(localWarnings);
            }
        });
        warnings.AddRange(packWarnings.Order(StringComparer.Ordinal));
        foreach ((string path, var result) in packResults)
            zero[FoxPath.NameToHash(path)] = result.Content;

        return new BuildPlan
        {
            Zero = WriteItems(inputs.BaselineZero, zero, inputs.CurrentZero),
            One = WriteItems(inputs.BaselineOne, one, inputs.CurrentOne),
            Foxfs = Foxfs.AddWmvs(inputs.BaseFoxfs, wmv),
            WmvHashes = wmv,
            GameDir = gameDir,
            Packs = packResults.ToDictionary(p => p.Key, p => p.Value.Record, StringComparer.Ordinal),
            Warnings = warnings,
            ModIds = mods.Select(m => m.Id).ToList(),
            MergedPacks = packResults.Values.Sum(r => r.Merges),
            ReusedPacks = packResults.Values.Count(r => r.Reused),
        };
    }

    private static (Content Content, PackRecord Record, bool Reused, int Merges) ResolvePack(
        string packPath, List<PackStep> steps, PlanInputs inputs, List<string> warnings)
    {
        bool fpkd = FpkArchive.IsFpkdName(packPath);
        ulong hash = FoxPath.NameToHash(packPath);

        // SnakeBite pulls the vanilla pack only for the first mod to place the pack, and only when it lists FpkEntries for it
        (QarArchive Archive, QarEntryInfo Entry)? vanilla = steps[0].ListsFpkEntries ? FindVanilla(hash, inputs.BaseArchives) : null;
        string vanillaId = vanilla is { } v ? $"{Path.GetFileName(v.Archive.Path)}:{Md5Key.ToHex(v.Entry.Md5)}" : "";
        string key = Md5Key.ToHex(Md5Key.Of(AnsiText.GetBytes(packPath + "|" + vanillaId + "|" +
            string.Join("|", steps.Select(s => $"{s.Mod.Id}:{Md5Key.ToHex(s.Mod.FileMd5(s.ZipName))}")))));
        var record = new PackRecord { Key = key, Steps = steps.Select(s => s.Mod.Id).ToList() };

        if (inputs.PreviousPacks.TryGetValue(packPath, out PackRecord? previous) && previous.Key == key && inputs.CurrentZero is { } current
            && current.TryGetEntry(hash, out QarEntryInfo existingEntry) && Md5Key.ToHex(existingEntry.Md5) == previous.Md5)
        {
            record.Md5 = previous.Md5;
            return (new ReusedContent(packPath, current, existingEntry), record, true, 0);
        }

        Content? content = null;
        int merges = 0;
        foreach (PackStep step in steps)
        {
            string modName = step.Mod.Metadata.Name;
            byte[]? existing = null;
            string existingSource = "";
            if (content != null)
            {
                existing = content is MergedContent m ? m.Data : ((ModFileContent)content).Mod.ReadFile(((ModFileContent)content).ZipName);
                existingSource = "earlier mod";
            }
            else if (vanilla is { } van)
            {
                existing = van.Archive.ReadContent(van.Entry);
                existingSource = Path.GetFileName(van.Archive.Path);
            }

            if (existing == null)
            {
                content = new ModFileContent(packPath, step.Mod, step.ZipName, step.Mod.FileMd5(step.ZipName));
                continue;
            }

            FpkArchive existingPack, modPack;
            try
            {
                existingPack = FpkArchive.Read(existing, inputs.FpkDictionary.Value);
                modPack = FpkArchive.Read(step.Mod.ReadFile(step.ZipName), inputs.FpkDictionary.Value);
            }
            catch (InvalidDataException e)
            {
                throw new InvalidDataException($"{modName}: couldn't combine {packPath} with the version from {existingSource} ({e.Message})", e);
            }
            foreach (string dropped in FpkMerge.DroppedNames(existingPack, modPack, fpkd))
                warnings.Add($"{modName}: {dropped} was left out when combining {packPath}, because the game doesn't read that file type there. That's harmless ({(fpkd ? "fpkd" : "fpk")} order table)");
            content = new MergedContent(packPath, FpkMerge.Merge(existingPack, modPack, fpkd).Write());
            merges++;
            Log.Debug($"[Plan] {packPath}: merged {modName} with {existingSource} ({modPack.Entries.Count} files into {existingPack.Entries.Count})");
        }

        record.Md5 = Md5Key.ToHex(ContentMd5(content!));
        return (content!, record, false, merges);
    }

    private static (QarArchive Archive, QarEntryInfo Entry)? FindVanilla(ulong hash, IReadOnlyList<QarArchive> archives)
    {
        foreach (QarArchive archive in archives)
            if (archive.TryGetEntry(hash, out QarEntryInfo entry))
                return (archive, entry);
        return null;
    }

    private static UInt128 ContentMd5(Content content) => content switch
    {
        ModFileContent file => file.Md5,
        MergedContent merged => Md5Key.Of(merged.Data),
        ReusedContent reused => reused.Entry.Md5,
        _ => throw new InvalidOperationException(),
    };

    /// <summary>Baseline entries not replaced by a mod, in baseline order, then mod files by path.</summary>
    private static List<QarWriteItem> WriteItems(QarArchive baseline, Dictionary<ulong, Content> contents, QarArchive? current)
    {
        var items = new List<QarWriteItem>();
        var baselineHashes = new HashSet<ulong>();
        foreach (QarEntryInfo entry in baseline.Entries)
        {
            if (contents.ContainsKey(entry.Hash) || !baselineHashes.Add(entry.Hash))
                continue;
            // the same entry in the current archive is used instead, so an unchanged archive is recognised and not rewritten
            QarSource source = current != null && current.TryGetEntry(entry.Hash, out QarEntryInfo same)
                && same.Md5 == entry.Md5 && same.StoredSize == entry.StoredSize && same.UncompressedSize == entry.UncompressedSize
                ? new RawQarSource(current, same)
                : new RawQarSource(baseline, entry);
            items.Add(new QarWriteItem(entry.Hash, $"baseline {entry.Hash:x16}", source));
        }

        var archives = new[] { current, baseline }.Where(a => a != null).Select(a => a!).ToList();
        foreach ((ulong hash, Content content) in contents.OrderBy(c => c.Value.Name, StringComparer.Ordinal))
        {
            if (content is ReusedContent reused)
            {
                items.Add(new QarWriteItem(hash, content.Name, new RawQarSource(reused.Archive, reused.Entry)));
                continue;
            }
            bool compress = FpkLayout.LastExtension(content.Name) is "fpk" or "fpkd";
            UInt128 md5 = ContentMd5(content);
            QarSource source = content switch
            {
                ModFileContent file => new FileQarSource(() => file.Mod.OpenFile(file.ZipName), file.Mod.FileSize(file.ZipName), compress, file.Md5),
                MergedContent merged => new BytesQarSource(merged.Data, compress),
                _ => throw new InvalidOperationException(),
            };
            foreach (QarArchive archive in archives)
                if (archive.TryGetEntry(hash, out QarEntryInfo existing) && existing.Md5 == md5 && existing.Compressed == compress)
                {
                    source = new RawQarSource(archive, existing);
                    break;
                }
            items.Add(new QarWriteItem(hash, content.Name, source));
        }
        return items;
    }
}
