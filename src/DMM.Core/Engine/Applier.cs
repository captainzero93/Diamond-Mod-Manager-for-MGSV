// Applies enabled mods: plan, build, stage, promote via journal
using System.Diagnostics;
using DMM.Core.Fpk;
using DMM.Core.Game;
using DMM.Core.Hashing;
using DMM.Core.Logging;
using DMM.Core.Mgsv;
using DMM.Core.Qar;
using DMM.Core.State;

namespace DMM.Core.Engine;

public sealed record ApplyResult(BuildPlan Plan, bool ZeroWritten, bool OneWritten, bool Chunk0Written, int FilesPlaced, int FilesRemoved, long Milliseconds);

public sealed class ApplyOptions
{
    /// <summary>Rebuild even if 00.dat/01.dat/chunk0.dat changed since DMM last wrote them.</summary>
    public bool Force { get; init; }
    /// <summary>Build with no mods but keep the on/off flags in the committed state (restore to retail).</summary>
    public bool NoMods { get; init; }
    /// <summary>Library id of a mod whose game folder files are written again even when they were edited since (reapply).</summary>
    public string? Reapply { get; init; }
    /// <summary>Told each step as it starts, in words for the status line.</summary>
    public Action<string>? Progress { get; init; }
}

public static class Applier
{
    public static ApplyResult Apply(GameLayout layout, ManagerState state, ModLibrary library, ApplyOptions options, Lazy<QarDictionary> qarDictionary, Lazy<FpkDictionary> fpkDictionary)
    {
        var timer = Stopwatch.StartNew();
        List<LibraryMod> mods = state.Mods.Where(m => m.Enabled && !options.NoMods).Select(m => LoadForApply(library, m)).ToList();
        Log.Info($"[Apply] Applying: {mods.Count} enabled mod(s): {string.Join(", ", mods.Select(m => m.Metadata.Name))}");
        Action<string> step = options.Progress ?? (_ => { });
        step($"Checking {(mods.Count == 1 ? "1 mod" : $"{mods.Count} mods")}");
        foreach (LibraryMod mod in mods)
        {
            List<string> broken = ModLibrary.CheckFiles(mod);
            if (broken.Count > 0)
                throw new InvalidDataException($"{mod.Metadata.Name}: its files are missing or have changed since you added it. Add it again from its .mgsv (DMM does this itself from the Mods folder when you apply), or remove it from the load order. ({string.Join(", ", broken.Take(5))})");
        }

        bool trusted = CurrentFilesTrusted(layout, state, options.Force);
        var baseArchives = new List<QarArchive>();
        var opened = new List<QarArchive>();
        try
        {
            step("Reading the game files");
            QarArchive baselineZero = Track(opened, QarArchive.Open(layout.BaselineZero));
            QarArchive baselineOne = Track(opened, QarArchive.Open(layout.BaselineOne));
            QarArchive? currentZero = trusted ? Track(opened, QarArchive.Open(layout.ZeroPath)) : null;
            QarArchive? currentOne = trusted ? Track(opened, QarArchive.Open(layout.OnePath)) : null;
            baseArchives.AddRange(OpenBaseArchives(layout, opened));
            Log.Info($"[Apply] Archives indexed at {timer.ElapsedMilliseconds} ms");

            var inputs = new PlanInputs
            {
                Layout = layout,
                BaselineZero = baselineZero,
                BaselineOne = baselineOne,
                BaseFoxfs = File.ReadAllBytes(layout.BaselineFoxfs),
                BaseArchives = baseArchives,
                CurrentZero = currentZero,
                CurrentOne = currentOne,
                PreviousPacks = trusted && state.Applied != null ? state.Applied.Packs : new Dictionary<string, PackRecord>(),
                QarDictionary = qarDictionary,
                FpkDictionary = fpkDictionary,
            };
            step("Working out where every file goes");
            BuildPlan plan = BuildPlanner.Plan(inputs, mods);
            foreach (string warning in plan.Warnings)
                Log.Warn("[Plan] " + warning);
            Log.Info($"[Apply] Planned at {timer.ElapsedMilliseconds} ms: 00.dat {plan.Zero.Count} entries, 01.dat {plan.One.Count} entries, " +
                     $"{plan.MergedPacks} pack merge(s), {plan.ReusedPacks} pack(s) unchanged, {plan.GameDir.Count} game folder file(s), {plan.WmvHashes.Count} custom WMV(s)");

            string zeroOut = GameLayout.BuildPath(layout.ZeroPath);
            string oneOut = GameLayout.BuildPath(layout.OnePath);
            string chunk0Out = GameLayout.BuildPath(layout.Chunk0Path);

            bool writeZero = currentZero == null || !SameAsCurrent(plan.Zero, currentZero);
            bool writeOne = currentOne == null || !SameAsCurrent(plan.One, currentOne);
            if (writeZero)
                step($"Building 00.dat ({plan.Zero.Count:n0} files)");
            if (writeZero)
                LogWrite("00.dat", QarWriter.Write(zeroOut, GameLayout.ZeroFlags, plan.Zero), timer);
            if (writeOne)
                step($"Building 01.dat ({plan.One.Count:n0} files)");
            if (writeOne)
                LogWrite("01.dat", QarWriter.Write(oneOut, GameLayout.OneFlags, plan.One), timer);

            bool writeChunk0;
            using (QarArchive chunk0 = QarArchive.Open(layout.Chunk0Path))
            {
                writeChunk0 = !Foxfs.Read(chunk0).AsSpan().SequenceEqual(plan.Foxfs);
                if (writeChunk0)
                    step("Updating chunk0.dat");
                if (writeChunk0)
                    LogWrite("chunk0.dat", QarWriter.Write(chunk0Out, chunk0.Flags, Chunk0Items(chunk0, plan.Foxfs)), timer);
            }

            var journal = new Journal(layout, "apply mods");
            if (writeZero) journal.Replace(zeroOut, layout.ZeroPath);
            if (writeOne) journal.Replace(oneOut, layout.OnePath);
            if (writeChunk0) journal.Replace(chunk0Out, layout.Chunk0Path);
            (int placed, int removed) = PlanGameDir(layout, state, plan, journal, options.Reapply);

            var newState = Clone(state);
            newState.Applied = new AppliedState
            {
                Utc = DateTime.UtcNow,
                Zero = writeZero ? FileFingerprint.Of(zeroOut) : state.Applied!.Zero,
                One = writeOne ? FileFingerprint.Of(oneOut) : state.Applied!.One,
                Chunk0 = writeChunk0 ? new FileFingerprint { Size = new FileInfo(chunk0Out).Length, LastWriteUtc = File.GetLastWriteTimeUtc(chunk0Out) }
                    : state.Applied?.Chunk0 ?? new FileFingerprint { Size = new FileInfo(layout.Chunk0Path).Length, LastWriteUtc = File.GetLastWriteTimeUtc(layout.Chunk0Path) },
                FoxfsMd5 = Md5Key.ToHex(Md5Key.Of(plan.Foxfs)),
                ModIds = plan.ModIds,
                ModPackages = mods.ToDictionary(m => m.Id, m => m.Manifest.PackageMd5, StringComparer.Ordinal),
                Packs = plan.Packs,
            };
            newState.GameDirFiles = NewGameDirState(layout, state, plan);
            journal.CommitState(newState);
            journal.SyncSnakeBite();
            journal.Delete(Path.Combine(layout.StagingDir, "gamedir"));

            foreach (QarArchive archive in opened)
                archive.Dispose();
            opened.Clear();
            step("Putting the new files in place");
            journal.Run();
            CopyInto(state, newState);
            Log.Info($"[Apply] Done in {timer.ElapsedMilliseconds} ms: 00.dat {(writeZero ? "rebuilt" : "unchanged")}, 01.dat {(writeOne ? "rebuilt" : "unchanged")}, " +
                     $"chunk0.dat {(writeChunk0 ? "rebuilt" : "unchanged")}, {placed} game folder file(s) written, {removed} removed");
            return new ApplyResult(plan, writeZero, writeOne, writeChunk0, placed, removed, timer.ElapsedMilliseconds);
        }
        finally
        {
            foreach (QarArchive archive in opened)
                archive.Dispose();
            mods.ForEach(m => m.Close()); // the mods folder files are not held open between applies
        }
    }

    private static LibraryMod LoadForApply(ModLibrary library, LibraryModRef mod)
    {
        try
        {
            return library.Load(mod.Id);
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or System.Xml.XmlException or InvalidOperationException)
        {
            throw new InvalidDataException($"{mod.Name}: DMM's copy of this mod is missing. Add it again from its .mgsv (DMM does this itself from the Mods folder when you apply), or remove it from the load order. ({e.Message})", e);
        }
    }

    private static QarArchive Track(List<QarArchive> opened, QarArchive archive)
    {
        opened.Add(archive);
        return archive;
    }

    public static List<QarArchive> OpenBaseArchives(GameLayout layout, List<QarArchive> opened)
    {
        string[] paths = layout.BaseArchives.ToArray();
        var archives = new QarArchive?[paths.Length];
        try
        {
            Parallel.For(0, paths.Length, i =>
            {
                if (File.Exists(paths[i]))
                    archives[i] = QarArchive.Open(paths[i]);
            });
        }
        catch (AggregateException e)
        {
            // close the ones that did open, and report the first real error rather than the wrapper
            foreach (QarArchive? archive in archives)
                archive?.Dispose();
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerExceptions[0]).Throw();
        }
        foreach (QarArchive? archive in archives)
            if (archive != null)
                opened.Add(archive);
        return archives.Where(a => a != null).Select(a => a!).ToList();
    }

    /// <summary>
    /// The current 00.dat/01.dat can supply raw entries only if they are what DMM last wrote. If they changed
    /// (SnakeBite or another tool wrote them), applying without force is refused, since those changes would be lost.
    /// </summary>
    private static bool CurrentFilesTrusted(GameLayout layout, ManagerState state, bool force)
    {
        if (state.Applied == null)
            return false;
        bool zeroOk = state.Applied.Zero.QuickMatch(layout.ZeroPath) || state.Applied.Zero.Matches(layout.ZeroPath);
        bool oneOk = state.Applied.One.QuickMatch(layout.OnePath) || state.Applied.One.Matches(layout.OnePath);
        if (zeroOk && oneOk)
            return true;
        string changed = string.Join(" and ", new[] { zeroOk ? null : "00.dat", oneOk ? null : "01.dat" }.Where(s => s != null));
        if (!force)
            throw new ExternalChangeException($"{changed} changed since DMM last applied (SnakeBite or another tool may have installed something). " +
                "Rebuilding them undoes those changes.");
        Log.Warn($"[Apply] {changed} changed outside DMM. Rebuilding from DMM's clean copies as asked");
        return false;
    }

    private static bool SameAsCurrent(List<QarWriteItem> items, QarArchive current)
    {
        if (items.Count != current.Entries.Count)
            return false;
        for (int i = 0; i < items.Count; i++)
            if (items[i].Source is not RawQarSource raw || raw.Archive != current || raw.Entry.Offset != current.Entries[i].Offset)
                return false;
        return true;
    }

    public static List<QarWriteItem> Chunk0Items(QarArchive chunk0, byte[] foxfs)
    {
        ulong foxfsHash = Foxfs.Hash;
        return chunk0.Entries.Select(e => e.Hash == foxfsHash
            ? new QarWriteItem(e.Hash, Foxfs.FileName, new BytesQarSource(foxfs, false))
            : new QarWriteItem(e.Hash, $"chunk0 {e.Hash:x16}", new RawQarSource(chunk0, e))).ToList();
    }

    private static void LogWrite(string name, QarWriteResult result, Stopwatch timer) =>
        Log.Info($"[Apply] {name}: {result.Reused} entries reused, {result.Encoded} encoded, {result.Length / 1024:n0} KB, at {timer.ElapsedMilliseconds} ms");

    /// <summary>Journals taking out the managed game folder files keep rejects, putting back the originals they replaced.</summary>
    internal static int RemoveGameDirFiles(GameLayout layout, ManagerState state, Func<string, bool> keep, Journal journal)
    {
        int removed = 0;
        foreach ((string relative, ManagedFile managed) in state.GameDirFiles)
        {
            if (keep(relative))
                continue;
            string target = layout.GameDirTarget(relative) ?? throw new InvalidDataException($"A mod file would go outside the game folder: {relative}");
            string? original = managed.HasOriginal ? Path.Combine(layout.GameDirOriginalsDir, ModLibrary.DiskName(relative)) : null;
            if (original != null && File.Exists(original))
                CheckTarget(layout, relative, target); // only a move back can jam; removing a file that is gone is a no-op
            journal.Remove(target, null, original != null && File.Exists(original) ? original : null);
            journal.Prune(Path.GetDirectoryName(target)!);
            removed++;
        }
        return removed;
    }

    // checked before anything is journaled: a move onto a folder, or under a file, would fail on every recovery
    private static void CheckTarget(GameLayout layout, string relative, string target)
    {
        for (string? dir = target; dir != null && dir.Length > layout.GameDir.Length; dir = Path.GetDirectoryName(dir))
            if (dir == target ? Directory.Exists(dir) : File.Exists(dir))
                throw new InvalidDataException($"{relative} can't be placed or removed, because the game folder has a {(dir == target ? "folder" : "file")} called {Path.GetRelativePath(layout.GameDir, dir)} in the way");
    }

    private static (int Placed, int Removed) PlanGameDir(GameLayout layout, ManagerState state, BuildPlan plan, Journal journal, string? reapply)
    {
        int placed = 0, removed = RemoveGameDirFiles(layout, state, plan.GameDir.ContainsKey, journal);

        string staging = Path.Combine(layout.StagingDir, "gamedir");
        if (Directory.Exists(staging))
            AtomicFile.DeleteDirectory(staging);
        foreach ((string relative, GameDirFile file) in plan.GameDir.OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
        {
            string target = layout.GameDirTarget(relative)!;
            CheckTarget(layout, relative, target);
            bool exists = File.Exists(target);
            state.GameDirFiles.TryGetValue(relative, out ManagedFile? managed);

            // the same content as last time: leave the file alone, including any edits made to it since (configs), unless reapplying its mod
            if (managed != null && managed.Md5 == file.Md5 && exists && file.ModId != reapply)
                continue;
            if (managed != null && exists && Md5Key.ToHex(Md5Key.OfFile(target)) == file.Md5)
                continue;

            string? backup = null;
            if (exists && managed == null)
            {
                backup = Path.Combine(layout.GameDirOriginalsDir, ModLibrary.DiskName(relative));
                Log.Info($"[Apply] {relative} exists and is not from a mod; keeping it in {layout.GameDirOriginalsDir} to restore later");
            }
            string staged = Path.Combine(staging, ModLibrary.DiskName(relative));
            Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
            using (Stream input = file.Mod.OpenFile(file.ZipName))
            using (var output = new FileStream(staged, FileMode.Create, FileAccess.Write))
            {
                input.CopyTo(output);
                output.Flush(flushToDisk: true);
            }
            if (Md5Key.ToHex(Md5Key.OfFile(staged)) != file.Md5)
                throw new InvalidDataException($"{relative}: this file in the mod changed since you added it. Add the mod again.");
            journal.Place(staged, target, backup);
            placed++;
        }
        return (placed, removed);
    }

    private static Dictionary<string, ManagedFile> NewGameDirState(GameLayout layout, ManagerState state, BuildPlan plan)
    {
        var files = new Dictionary<string, ManagedFile>(StringComparer.OrdinalIgnoreCase);
        foreach ((string relative, GameDirFile file) in plan.GameDir)
        {
            state.GameDirFiles.TryGetValue(relative, out ManagedFile? old);
            string target = layout.GameDirTarget(relative)!;
            bool hasOriginal = old?.HasOriginal ?? File.Exists(target);
            files[relative] = new ManagedFile { Owner = file.ModId, Md5 = file.Md5, HasOriginal = hasOriginal };
        }
        return files;
    }

    private static ManagerState Clone(ManagerState state) =>
        System.Text.Json.JsonSerializer.Deserialize(System.Text.Json.JsonSerializer.Serialize(state, StateJsonContext.Default.ManagerState), StateJsonContext.Default.ManagerState)!;

    private static void CopyInto(ManagerState target, ManagerState source)
    {
        target.Applied = source.Applied;
        target.GameDirFiles = new Dictionary<string, ManagedFile>(source.GameDirFiles, StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class ExternalChangeException(string message) : Exception(message);
