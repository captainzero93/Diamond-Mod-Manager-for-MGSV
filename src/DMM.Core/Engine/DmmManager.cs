// Entry point: one game folder, its state, library and operations
using DMM.Core.Fpk;
using DMM.Core.Game;
using DMM.Core.Hashing;
using DMM.Core.Logging;
using DMM.Core.Mgsv;
using DMM.Core.State;

namespace DMM.Core.Engine;

public sealed class DmmManager : IDisposable
{
    private readonly FileStream? lockStream;
    private readonly Dictionary<string, LibraryMod> loaded = new(StringComparer.Ordinal);

    public GameLayout Layout { get; }
    public ModLibrary Library { get; }
    public ManagerState? State { get; private set; }
    public Lazy<QarDictionary> QarDictionary { get; }
    public Lazy<FpkDictionary> FpkDictionary { get; }

    private DmmManager(GameLayout layout, FileStream lockStream, string dataDir)
    {
        Layout = layout;
        this.lockStream = lockStream;
        Library = new ModLibrary(layout.LibraryDir);
        QarDictionary = new Lazy<QarDictionary>(() =>
        {
            using TextReader reader = DictionaryFile.Open(dataDir, "qar_dictionary.txt");
            return Hashing.QarDictionary.Load(reader);
        });
        FpkDictionary = new Lazy<FpkDictionary>(() =>
        {
            using TextReader reader = DictionaryFile.Open(dataDir, "fpk_dictionary.txt");
            return Hashing.FpkDictionary.Load(reader);
        });
    }

    /// <summary>
    /// Opens a game folder: takes the single-instance lock, finishes any interrupted operation and loads the state.
    /// The dictionaries are built in; a qar_dictionary.txt or fpk_dictionary.txt in dataDir (the program folder by default) replaces them.
    /// </summary>
    public static DmmManager Open(string gameDir, string? dataDir = null)
    {
        var layout = new GameLayout(gameDir);
        if (!layout.LooksLikeGame)
            throw new DirectoryNotFoundException($"{layout.GameDir} doesn't look like the MGSV game folder (it needs mgsvtpp.exe, master\\0\\00.dat, master\\0\\01.dat and master\\chunk0.dat).");
        MoveLegacyStateDir(layout);
        Directory.CreateDirectory(layout.StateDir);
        FileStream lockStream;
        try
        {
            lockStream = new FileStream(layout.LockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose); // gone when DMM closes
        }
        catch (IOException)
        {
            throw new IOException("DMM is already running for this game folder.");
        }

        var manager = new DmmManager(layout, lockStream, dataDir ?? AppContext.BaseDirectory);
        try
        {
            Log.Open(layout.LogDir);
            Log.Debug($"[Open] {layout.GameDir}, game version {layout.GameVersion()}");
            string? recovered = Journal.Recover(layout);
            if (recovered != null)
            {
                Log.Warn($"[Open] Finished the interrupted \"{recovered}\"");
                manager.StartupNote = $"DMM was closed part way through \"{recovered}\" last time. The game files had already started changing, so DMM finished that operation now and they're as intended.";
            }
            else if (manager.DeleteStrayBuildFiles())
            {
                manager.StartupNote = "DMM was closed while building new game files last time. The game files had not been touched yet, so nothing changed, and the half-built files were deleted. Apply again to make the change.";
            }
            manager.Library.Tidy();
            manager.State = StateStore.Load(layout.StateFile);
            return manager;
        }
        catch
        {
            manager.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Builds before 0.9.3 kept their state in game\NewBite. It moves to game\D-Mod once, after any unfinished journal
    /// there is completed, since a journal holds full paths into the folder it was written in.
    /// </summary>
    private static void MoveLegacyStateDir(GameLayout layout)
    {
        if (!Directory.Exists(layout.LegacyStateDir) || Directory.Exists(layout.StateDir))
            return;
        var legacy = new GameLayout(layout.GameDir, GameLayout.LegacyStateFolder);
        string legacyLock = Path.Combine(legacy.StateDir, "newbite.lock");
        try
        {
            using (new FileStream(legacyLock, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                if (Journal.Recover(legacy) is { } recovered)
                    Log.Warn($"[Open] Finished the interrupted \"{recovered}\" in {legacy.StateDir} before moving it");
            }
            File.Delete(legacyLock);
            Directory.Move(legacy.StateDir, layout.StateDir);
        }
        catch (IOException e)
        {
            throw new IOException($"DMM couldn't move {legacy.StateDir} to {layout.StateDir}. Close any older NewBite still open for this game, then try again. ({e.Message})", e);
        }
    }

    public GameStatus Status() => GameInspector.Inspect(Layout, State);

    /// <summary>True when the enabled mods, their order or their packages differ from what was last applied.</summary>
    public bool HasPendingChanges
    {
        get
        {
            // during a restore the last apply had every mod off on purpose, and nothing can be applied until it is finished
            if (State == null || State.AwaitingSteamVerify)
                return false;
            List<string> enabled = State.Mods.Where(m => m.Enabled).Select(m => m.Id).ToList();
            AppliedState? applied = State.Applied;
            if (applied == null)
                return true; // never applied, e.g. a take over whose apply failed: SnakeBite's mods are still in the game
            return !applied.ModIds.SequenceEqual(enabled)
                || enabled.Any(id => !applied.ModPackages.TryGetValue(id, out string? md5) || Mod(id)?.Manifest.PackageMd5 != md5);
        }
    }

    /// <summary>The mods folder files are looked for in when the one a mod was added from is gone; set again when it changes.</summary>
    public void SetModsFolder(string folder)
    {
        if (string.Equals(Library.ModsFolder, folder, StringComparison.OrdinalIgnoreCase))
            return;
        Library.ModsFolder = folder;
        // copies already loaded were matched against the old folder
        loaded.Clear();
        missingPackages.Clear();
    }

    /// <summary>Library mod by id, cached; null when its library folder is missing or unreadable.</summary>
    public LibraryMod? Mod(string id)
    {
        if (loaded.TryGetValue(id, out LibraryMod? mod))
            return mod;
        try
        {
            mod = Library.Load(id);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or UnauthorizedAccessException)
        {
            Log.Warn($"[Library] {id}: DMM's copy of this mod can't be read ({e.Message})");
            return null;
        }
        loaded[id] = mod;
        return mod;
    }

    public void Setup(bool keepRetailCopies)
    {
        RequireGameClosed();
        FinishPending();
        GameStatus status = Status();
        if (status.State is not (GameFilesState.Retail or GameFilesState.RetailWithState))
            throw new InvalidOperationException($"Setup needs unmodded game files. {status.Detail}");
        if (State?.AwaitingSteamVerify == true)
            throw new InvalidOperationException("A restore to retail is waiting to be finished. Finish it first, or it will be lost.");
        if (!Layout.IsSteamInstall && !keepRetailCopies)
        {
            keepRetailCopies = true;
            Log.Info("[Setup] Not a Steam install: retail copies are always kept, as Steam verify cannot restore the files");
        }
        if (State == null)
            ClearLibrary();
        Guarded(() => Engine.Setup.Run(Layout, State, QarDictionary.Value, keepRetailCopies));
        State = StateStore.Load(Layout.StateFile);
    }

    // a restore to retail is a clean slate: the next setup starts with an empty load order, so library copies of the old one go
    private void ClearLibrary()
    {
        loaded.Clear();
        if (!Directory.Exists(Library.Dir))
            return;
        string[] folders = Directory.GetDirectories(Library.Dir);
        foreach (string folder in folders)
            AtomicFile.DeleteDirectory(folder);
        if (folders.Length > 0)
            Log.Info($"[Setup] Removed {folders.Length} library copies left from before the restore; add mods again from the mods folder");
    }

    public TakeOverCheck CheckTakeOver()
    {
        RequireStatus(GameFilesState.PreparedBySnakeBite);
        return TakeOver.Check(Layout, QarDictionary.Value);
    }

    /// <summary>Takes over a SnakeBite install and applies at once, so SnakeBite's mods leave the game now.</summary>
    public ApplyResult TakeOverSnakeBite()
    {
        RequireGameClosed();
        FinishPending();
        RequireStatus(GameFilesState.PreparedBySnakeBite);
        return Guarded(() =>
        {
            State = TakeOver.Run(Layout, QarDictionary.Value);
            return Applier.Apply(Layout, State, Library, new ApplyOptions { Force = true }, QarDictionary, FpkDictionary);
        });
    }

    /// <summary>
    /// Adds mods to the end of the load order, enabled. A mod with the same name is replaced where it stands, keeping its
    /// enabled state.
    /// </summary>
    public List<AddModResult> AddMods(IEnumerable<ModSource> sources, bool sameBuildOnly = false)
    {
        FinishPending();
        ManagerState state = RequireState();
        var results = new List<AddModResult>();
        Version gameVersion = Layout.GameVersion();
        foreach (ModSource source in sources)
        {
            string? same = sameBuildOnly ? Mod(ModLibrary.MakeId(source.Metadata.Name))?.Manifest.PackageMd5 : null;
            AddModResult result = Library.Add(source, gameVersion, Path.Combine(Layout.StagingDir, "incoming"), QarDictionary.Value, same);
            loaded[result.Mod.Id] = result.Mod;
            LibraryModRef? existing = state.Mods.FirstOrDefault(m => m.Id == result.Mod.Id);
            if (existing == null)
                state.Mods.Add(existing = new LibraryModRef { Id = result.Mod.Id, Enabled = true });
            existing.Name = result.Mod.Metadata.Name;
            existing.Version = result.Mod.Metadata.Version;
            // a .mgsv taken out of a .zip is read from its own file now, so that is the file named (and asked for if it goes)
            bool fromOwnFile = source.InnerEntry != null && Path.IsPathRooted(result.Mod.Manifest.PackagePath ?? "");
            existing.SourceFileName = fromOwnFile ? Path.GetFileName(result.Mod.PackageFile!) : source.DisplayFile;
            Log.Info($"[Library] {(result.Replaced ? "Updated" : "Added")} {existing.Name} {existing.Version} from {source.FilePath}{(source.InnerEntry != null ? " > " + source.InnerEntry : "")}");
            foreach (string warning in result.Warnings)
                Log.Warn($"[Library] {existing.Name}: {warning}");
            // a .mgsv inside a .zip cannot be read in place quickly: it is taken out into the Mods folder, or kept in D-Mod when that fails
            if (fromOwnFile && result.TakenOutOfZip)
                Log.Warn($"[Library] {existing.Name}: took {Path.GetFileName(result.Mod.PackageFile)} out of {Path.GetFileName(source.FilePath)} and put it in the Mods folder. " +
                         "The .zip is still there. Once you've added everything you want from it, you can delete it.");
            else if (fromOwnFile)
                Log.Info($"[Library] {existing.Name}: reading {Path.GetFileName(result.Mod.PackageFile)} from the Mods folder, taken out of {Path.GetFileName(source.FilePath)} before");
            else if (source.InnerEntry != null)
                Log.Warn($"[Library] {existing.Name} came in a .zip, so DMM keeps a copy of its .mgsv in the game's D-Mod folder. " +
                         $"To save the space, take {Path.GetFileName(source.InnerEntry)} out of the .zip, put it in the Mods folder and add that instead.");
            results.Add(result);
            StateStore.Save(Layout.StateFile, state);
        }
        return results;
    }

    public List<AddModResult> AddMods(IEnumerable<string> paths) => AddMods(paths.SelectMany(ModSources.Read).ToList());

    /// <summary>Removes a mod from the load order. Its files leave the game on the next apply; its library copy goes then.</summary>
    public void RemoveMod(string id)
    {
        FinishPending();
        ManagerState state = RequireState();
        LibraryModRef mod = Find(id);
        Guarded(() =>
        {
            state.Mods.Remove(mod);
            StateStore.Save(Layout.StateFile, state);
        });
        if (state.Applied?.ModIds.Contains(mod.Id) != true)
        {
            loaded.Remove(mod.Id);
            try { Library.Remove(mod.Id); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"[Library] {mod.Id}: couldn't delete DMM's copy yet, it's tried again after the next apply ({e.Message})"); // the removal itself is saved
            }
        }
        Log.Info($"[Library] Removed {mod.Name}");
    }

    public void SetEnabled(string id, bool enabled)
    {
        FinishPending();
        Guarded(() =>
        {
            Find(id).Enabled = enabled;
            StateStore.Save(Layout.StateFile, State!);
        });
    }

    public void SetEnabled(IReadOnlyCollection<string> ids, bool enabled)
    {
        FinishPending();
        Guarded(() =>
        {
            foreach (string id in ids)
                Find(id).Enabled = enabled;
            StateStore.Save(Layout.StateFile, State!);
        });
    }

    public void SetAllEnabled(bool enabled)
    {
        FinishPending();
        ManagerState state = RequireState();
        Guarded(() =>
        {
            state.Mods.ForEach(m => m.Enabled = enabled);
            StateStore.Save(Layout.StateFile, state);
        });
    }

    /// <summary>Sets the load order. ids must list every mod once.</summary>
    public void SetOrder(IReadOnlyList<string> ids)
    {
        FinishPending();
        ManagerState state = RequireState();
        if (ids.Count != state.Mods.Count || ids.Distinct().Count() != ids.Count || ids.Any(id => state.Mods.All(m => m.Id != id)))
            throw new ArgumentException("The new order must list every mod exactly once.");
        Guarded(() =>
        {
            state.Mods = ids.Select(id => state.Mods.First(m => m.Id == id)).ToList();
            StateStore.Save(Layout.StateFile, state);
        });
    }

    /// <summary>
    /// Enabled mods whose library copy (game\D-Mod\library) is missing or damaged. Apply builds from those copies, so
    /// they have to be added again from their .mgsv first.
    /// </summary>
    public List<LibraryModRef> DamagedLibraryCopies()
    {
        var damaged = new List<LibraryModRef>();
        foreach (LibraryModRef mod in State?.Mods ?? [])
        {
            if (!mod.Enabled)
                continue;
            loaded.Remove(mod.Id);
            LibraryMod? copy = Mod(mod.Id);
            bool ok;
            try { ok = copy != null && ModLibrary.CheckFiles(copy).Count == 0; }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { ok = false; }
            if (!ok)
            {
                Log.Warn($"[Library] {mod.Name} {mod.Version}: its files are missing or have changed since it was added");
                damaged.Add(mod);
            }
        }
        return damaged;
    }

    /// <summary>
    /// Mods folder files for load order mods whose library copy still holds extracted files (made before 0.9.45), or a copy of
    /// a .mgsv from a .zip (added before 0.9.82). Adding them again switches the copy to read from the file in the Mods folder
    /// (a .zip's .mgsv is taken out into it first), which frees the space in D-Mod.
    /// </summary>
    // ponytail: a zip copy is only freed while its .zip (or the .mgsv) is still in the Mods folder; otherwise it stays
    public List<ModSource> ExtractedCopySources(IEnumerable<ModSource> sources) =>
        (State?.Mods ?? []).Where(m => Directory.Exists(Path.Combine(Library.Dir, m.Id, "files"))
                || (Library.ModsFolder != null && File.Exists(Path.Combine(Library.Dir, m.Id, "mod.mgsv"))))
            .Select(m => FindSource(m, sources)).OfType<ModSource>().ToList();

    /// <summary>The mods folder file that holds this mod at the same version, if any.</summary>
    public static ModSource? FindSource(LibraryModRef mod, IEnumerable<ModSource> sources) =>
        sources.FirstOrDefault(s => s.Metadata.Name == mod.Name && s.Metadata.Version == mod.Version);

    /// <summary>Files the enabled mods share, in load order: what an apply resolves by order. Read from metadata.xml.</summary>
    public ConflictReport Conflicts() => Scan(Metadata(enabledOnly: true));

    /// <summary>Files any two mods in the load order would share if both were on, switched-off mods included.</summary>
    public ConflictReport PotentialConflicts() => Scan(Metadata(enabledOnly: false));

    /// <summary>Conflicts the enabled mods would have with this mod added at the end (or replacing the same-name mod).</summary>
    public ConflictReport PreviewConflicts(ModEntry candidate, string candidateId)
    {
        var mods = Metadata(enabledOnly: true).Where(m => m.Id != candidateId).ToList();
        mods.Add((candidateId, candidate));
        return Scan(mods);
    }

    // packages opened for unlisted pack contents are closed once per scan, so the mods folder files are not held open after it
    private ConflictReport Scan(List<(string Id, ModEntry Metadata)> mods)
    {
        try
        {
            return ConflictScanner.Scan(mods, QarDictionary.Value, PackContents);
        }
        finally
        {
            foreach (LibraryMod? mod in loaded.Values)
                mod?.Close();
        }
    }

    private List<(string Id, ModEntry Metadata)> Metadata(bool enabledOnly) =>
        (State?.Mods ?? []).Where(m => m.Enabled || !enabledOnly).Select(m => (m.Id, Mod(m.Id)?.Metadata)).Where(m => m.Metadata != null).Select(m => (m.Id, m.Metadata!)).ToList();

    private readonly Dictionary<string, List<string>?> packContents = new(StringComparer.Ordinal);
    private readonly HashSet<string> missingPackages = new(StringComparer.Ordinal);

    // files inside a pack a mod ships without listing them in metadata.xml, read once from its library copy
    private IEnumerable<string>? PackContents(string id, ModQarFile file)
    {
        // a preview's mod has no library copy yet, so its unlisted pack counts as shared whole
        if (State?.Mods.Any(m => m.Id == id) != true || Mod(id) is not { } mod)
            return null;
        // one note per mod, not one per pack, when its .mgsv is gone; not cached, so it is read once the file is back
        if (mod.PackageFile is { } package && !File.Exists(package))
        {
            if (missingPackages.Add(id))
                Log.Warn($"[Conflicts] {mod.Metadata.Name}: {(Path.IsPathRooted(mod.Manifest.PackagePath!) ? "its .mgsv isn't in the Mods folder" : "DMM's copy of it is missing")}, so DMM can't look inside it to check for clashes. " +
                         $"Until it's back, some of the clashes the Conflicts list shows for it may not be real ({package})");
            return null;
        }
        string zipName = ModFiles.ZipName(file, mod.HasFile);
        string key = $"{id}|{mod.Manifest.PackageMd5}|{zipName}";
        if (packContents.TryGetValue(key, out List<string>? cached))
            return cached;
        List<string>? names = null;
        try
        {
            if (mod.HasFile(zipName))
                names = FpkArchive.Read(mod.ReadFile(zipName), FpkDictionary.Value).Entries.Select(e => FoxPath.ToWinPath(e.Name)).ToList();
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or OverflowException)
        {
            Log.Warn($"[Conflicts] {mod.Metadata.Name}: couldn't read part of the mod to check for clashes, so some of the clashes the Conflicts list shows for it may not be real ({file.QarPath}: {e.Message})");
        }
        packContents[key] = names;
        return names;
    }

    /// <param name="reapply">Library id of a mod whose game folder files are written again over any edits made to them.</param>
    /// <param name="progress">Told each step as it starts ("Building 00.dat (412 files)"), for a progress line.</param>
    public ApplyResult Apply(bool force = false, string? reapply = null, Action<string>? progress = null)
    {
        RequireGameClosed();
        FinishPending();
        ManagerState state = RequireState();
        if (state.AwaitingSteamVerify)
            throw new InvalidOperationException($"A restore to retail is in progress. {Layout.RetailFilesHint} and finish it, or run setup if the retail files are already back.");
        GameStatus status = Status();
        if (status.State is not (GameFilesState.Managed or GameFilesState.ChangedOutside))
            throw new InvalidOperationException(status.Detail);
        ApplyResult result = Guarded(() => Applier.Apply(Layout, state, Library, new ApplyOptions { Force = force, Reapply = reapply, Progress = progress }, QarDictionary, FpkDictionary));
        PruneLibrary();
        return result;
    }

    /// <summary>True when the game is retail again (retail copies used); false when Steam verify and FinishRestoreRetail are needed.</summary>
    public bool BeginRestoreRetail()
    {
        RequireGameClosed();
        FinishPending();
        if (RequireState().AwaitingSteamVerify)
            throw new InvalidOperationException("A restore to retail is already waiting to be finished.");
        GameStatus status = Status();
        if (status.State is not (GameFilesState.Managed or GameFilesState.ChangedOutside))
            throw new InvalidOperationException(status.Detail);
        bool done = Guarded(() => RetailRestore.Begin(Layout, RequireState(), Library, QarDictionary, FpkDictionary));
        if (done)
            State = null;
        return done;
    }

    /// <summary>
    /// True when a restore waits for Steam verify and 00.dat, 01.dat and chunk0.dat hash to the retail files recorded at
    /// setup, so finishing cannot remove a_chunk7.dat while a half-verified 00.dat still needs it. False without a record.
    /// </summary>
    public bool RetailFilesBack()
    {
        if (State is not { AwaitingSteamVerify: true, RetailZero: { } zero, RetailOne: { } one, RetailChunk0: { } chunk0 })
            return false;
        try
        {
            return chunk0.Matches(Layout.Chunk0Path) && zero.Matches(Layout.ZeroPath) && one.Matches(Layout.OnePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false; // Steam still has them open, or is replacing one
        }
    }

    /// <summary>
    /// Drops a restore waiting for Steam verify. The mods were taken out by an apply and their on/off flags kept, so applying
    /// puts them back; if Steam already replaced the files, the status asks for setup instead.
    /// </summary>
    public void CancelRestoreRetail()
    {
        FinishPending();
        ManagerState state = RequireState();
        Guarded(() =>
        {
            state.AwaitingSteamVerify = false;
            StateStore.Save(Layout.StateFile, state);
        });
        Log.Info("[Restore] Cancelled; the load order is kept and can be applied again");
    }

    /// <summary>Broken game files with retail copies that match what setup recorded: puts the game back to retail without Steam.</summary>
    public void RestoreRetailCopiesOverBroken()
    {
        RequireGameClosed();
        FinishPending();
        ManagerState state = RequireState();
        if (Status().State != GameFilesState.Broken)
            throw new InvalidOperationException("The game files aren't broken. Use Restore to retail instead.");
        if (!RetailCopies.Valid(Layout, state, full: true))
            throw new InvalidOperationException($"The retail copies (*.original) don't match what DMM recorded at setup, so they can't be used. {Layout.RetailFilesHint}, then set up.");
        Guarded(() => RetailRestore.RestoreCopiesOverBroken(Layout, state));
        State = null;
    }

    public string FinishRestoreRetail()
    {
        RequireGameClosed();
        FinishPending();
        string note = Guarded(() => RetailRestore.Finish(Layout, RequireState()));
        State = null;
        return note;
    }

    private void PruneLibrary()
    {
        // mods removed from the load order are deleted from the library once the game no longer has their files
        if (State == null || !Directory.Exists(Library.Dir))
            return;
        var keep = new HashSet<string>(State.Mods.Select(m => m.Id));
        foreach (string dir in Directory.GetDirectories(Library.Dir))
        {
            string id = Path.GetFileName(dir);
            if (keep.Contains(id) || id.Contains('.'))
                continue;
            try
            {
                Library.Remove(id);
                loaded.Remove(id);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"[Library] {id}: couldn't delete DMM's old copy, it's tried again after the next apply ({e.Message})"); // the apply itself is done
            }
        }
    }

    /// <summary>What a crash or forced close last time left, and what was done about it; null after a clean exit.</summary>
    public string? StartupNote { get; private set; }

    private bool DeleteStrayBuildFiles()
    {
        bool found = false;
        foreach (string target in new[] { Layout.ZeroPath, Layout.OnePath, Layout.Chunk0Path, Layout.Chunk7Path, Layout.Texture7Path, Layout.BaselineZero, Layout.BaselineOne, Layout.BaselineFoxfs }
                     .Concat(RetailCopies.Paths(Layout)))
        {
            string build = GameLayout.BuildPath(target);
            if (File.Exists(build))
            {
                Log.Warn($"[Open] Deleting unfinished build file {build}");
                File.SetAttributes(build, FileAttributes.Normal);
                File.Delete(build);
                found = true;
            }
        }
        if (Directory.Exists(Layout.StagingDir))
            AtomicFile.DeleteDirectory(Layout.StagingDir);
        return found;
    }

    /// <summary>
    /// A journal left by an operation that failed part way is finished before anything else changes, and the state is read
    /// again from disk, so a stale in-memory state never overwrites what the journal recorded.
    /// </summary>
    private void FinishPending()
    {
        if (!File.Exists(Layout.JournalFile))
            return;
        string? recovered = Journal.Recover(Layout);
        Log.Warn($"[Journal] Finished the interrupted \"{recovered}\" before going on");
        ReloadState();
    }

    private T Guarded<T>(Func<T> work)
    {
        try
        {
            return work();
        }
        catch
        {
            ReloadState();
            throw;
        }
    }

    private void Guarded(Action work) => Guarded(() => { work(); return 0; });

    private void ReloadState()
    {
        loaded.Clear();
        try { State = StateStore.Load(Layout.StateFile); }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            Log.Error($"[State] Couldn't reload DMM's records after a failed operation ({Layout.StateFile}: {e.Message})");
        }
    }

    private ManagerState RequireState() =>
        State ?? throw new InvalidOperationException("DMM isn't set up for this game yet. Set up the game files, or take over the SnakeBite install, first.");

    private void RequireStatus(GameFilesState expected)
    {
        GameStatus status = Status();
        if (status.State != expected)
            throw new InvalidOperationException(status.Detail);
    }

    private static void RequireGameClosed()
    {
        // mgsvtpp.exe can take a few seconds to exit after the window closes
        for (int waited = 0; GameLayout.IsGameRunning(); waited += 500)
        {
            if (waited >= 15000)
                throw new InvalidOperationException("MGSV (mgsvtpp.exe) is still running. It can stay open in the background for a while after the game closes, or after a crash. " +
                    "Wait a moment, or press Close MGSV in the sidebar to end it, then try again.");
            Thread.Sleep(500);
        }
    }

    private LibraryModRef Find(string idOrName)
    {
        ManagerState state = RequireState();
        return state.Mods.FirstOrDefault(m => m.Id == idOrName)
               ?? state.Mods.FirstOrDefault(m => string.Equals(m.Name, idOrName, StringComparison.OrdinalIgnoreCase))
               ?? throw new KeyNotFoundException($"{idOrName} isn't in the load order");
    }

    public string ResolveId(string idOrName) => Find(idOrName).Id;

    public void Dispose()
    {
        lockStream?.Dispose();
        Log.Close();
    }
}
