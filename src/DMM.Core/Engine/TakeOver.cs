// Takes over a SnakeBite-prepared game
using DMM.Core.Game;
using DMM.Core.Hashing;
using DMM.Core.Logging;
using DMM.Core.Mgsv;
using DMM.Core.Qar;
using DMM.Core.State;

namespace DMM.Core.Engine;

public sealed record TakeOverCheck(
    int SnakeBiteMods, int ZeroKept, int ZeroDropped, int OneKept, int OneDropped, int GameDirFiles,
    bool FoxfsFromOriginal, string? SplitArchivesProblem, bool RetailBackups, IReadOnlyList<string> SnakeBiteExtras);

/// <summary>
/// After SnakeBite setup, 00.dat holds only foxpatch.dat and 01.dat only the lua files moved out of the retail 00.dat;
/// a_chunk7.dat and a_texture7.dat hold the rest of the retail files. Taking over keeps exactly that and drops everything
/// else, so mods SnakeBite installed (and files its uninstall left behind) are gone. Game folder files SnakeBite recorded
/// become DMM's to remove. Mods are added again afterwards. No download needed unless a_chunk7.dat or a_texture7.dat
/// look damaged; then Steam verify and setup are the way back.
/// </summary>
public static class TakeOver
{
    // SnakeBite SettingsManager.vanillaDatHash: MD5(00.dat) + MD5(01.dat) of the retail 1.0.15.4 files
    public const string RetailDatHash1_0_15_4 = "B28BEDBC7B0A66F43CAFF81D2ECD96DF1240EF2F06D9D193851C5AE45D8D46BF";

    // SnakeBite ModManager.Checkc7t7Setup minimum sizes; tests on small synthetic games lower them
    internal static long MinChunk7Size = 492000000;
    internal static long MinTexture7Size = 260000000;

    public static TakeOverCheck Check(GameLayout layout, QarDictionary dictionary)
    {
        SnakeBiteSettings settings = LoadSettings(layout);
        int zeroKept, zeroDropped, oneKept, oneDropped;
        using (QarArchive zero = QarArchive.Open(layout.ZeroPath))
        {
            zeroKept = Baseline(zero, dictionary, zeroArchive: true).Count;
            zeroDropped = zero.Entries.Count - zeroKept;
        }
        using (QarArchive one = QarArchive.Open(layout.OnePath))
        {
            oneKept = Baseline(one, dictionary, zeroArchive: false).Count;
            oneDropped = one.Entries.Count - oneKept;
        }
        int gameDirFiles = GameDirFiles(settings).Count(f => layout.GameDirTarget(f.Path) is { } t && File.Exists(t));
        var extras = new[] { layout.ZeroPath + ".original", layout.OnePath + ".original", layout.SnakeBiteChunk0Original, Path.Combine(layout.GameDir, "RevertChanges.MGSVPreset") }
            .Where(File.Exists).ToList();
        return new TakeOverCheck(settings.Mods.Count, zeroKept, zeroDropped, oneKept, oneDropped, gameDirFiles,
            OriginalFoxfs(layout) != null, SplitArchivesProblem(layout), RetailCopies.Valid(layout, null), extras);
    }

    /// <summary>Writes the baseline and state. The caller applies straight after, which removes SnakeBite's files.</summary>
    public static ManagerState Run(GameLayout layout, QarDictionary dictionary)
    {
        if (SplitArchivesProblem(layout) is { } problem)
            throw new InvalidDataException($"{problem} {layout.RetailFilesHint}, then run setup.");
        SnakeBiteSettings settings = LoadSettings(layout);
        Directory.CreateDirectory(layout.BaselineDir);

        using (QarArchive zero = QarArchive.Open(layout.ZeroPath))
        {
            List<QarEntryInfo> kept = Baseline(zero, dictionary, zeroArchive: true);
            if (kept.Count != 1)
                throw new InvalidDataException($"00.dat has no foxpatch.dat, so SnakeBite didn't set it up. {layout.RetailFilesHint}, then set up.");
            QarWriter.Write(GameLayout.BuildPath(layout.BaselineZero), GameLayout.ZeroFlags,
                kept.Select(e => new QarWriteItem(e.Hash, "foxpatch.dat", new RawQarSource(zero, e))).ToList());
            Log.Info($"[TakeOver] 00.dat: keeping foxpatch.dat, dropping {zero.Entries.Count - 1} SnakeBite entries");
        }
        using (QarArchive one = QarArchive.Open(layout.OnePath))
        {
            List<QarEntryInfo> kept = Baseline(one, dictionary, zeroArchive: false);
            QarWriter.Write(GameLayout.BuildPath(layout.BaselineOne), GameLayout.OneFlags,
                kept.Select(e => new QarWriteItem(e.Hash, dictionary.GzsName(e.Hash), new RawQarSource(one, e))).ToList());
            Log.Info($"[TakeOver] 01.dat: keeping {kept.Count} lua files, dropping {one.Entries.Count - kept.Count} SnakeBite entries");
        }

        byte[] baseFoxfs;
        if (OriginalFoxfs(layout) is { } original)
        {
            baseFoxfs = Foxfs.AddChunks(original);
            AtomicFile.WriteAllBytes(layout.RetailFoxfs, original);
            Log.Info("[TakeOver] foxfs.dat base made from chunk0.dat.original");
        }
        else
        {
            using QarArchive chunk0 = QarArchive.Open(layout.Chunk0Path);
            baseFoxfs = Foxfs.RemoveWmvs(Foxfs.Read(chunk0), settings.Mods.SelectMany(m => m.ModWmvEntries).Select(w => w.Hash));
            Log.Info("[TakeOver] foxfs.dat base made from the current one with SnakeBite's custom video lines removed");
        }
        AtomicFile.WriteAllBytes(GameLayout.BuildPath(layout.BaselineFoxfs), baseFoxfs);

        var state = new ManagerState { BaselineSource = "snakebite", BaselineCreatedUtc = DateTime.UtcNow, GameVersion = layout.GameVersion().ToString() };
        foreach ((string path, string? contentHash) in GameDirFiles(settings))
        {
            string? target = layout.GameDirTarget(path);
            if (target == null || !File.Exists(target))
                continue;
            // the mod's own MD5 (MakeBite ContentHash) when known
            string md5 = contentHash is { Length: 32 } ? contentHash.ToUpperInvariant() : Md5Key.ToHex(Md5Key.OfFile(target));
            state.GameDirFiles[path] = new ManagedFile { Owner = "snakebite", Md5 = md5, HasOriginal = false };
        }

        var journal = new Journal(layout, "take over SnakeBite install");
        journal.Replace(GameLayout.BuildPath(layout.BaselineZero), layout.BaselineZero);
        journal.Replace(GameLayout.BuildPath(layout.BaselineOne), layout.BaselineOne);
        journal.Replace(GameLayout.BuildPath(layout.BaselineFoxfs), layout.BaselineFoxfs);
        journal.CommitState(state);
        journal.Run();
        Log.Info($"[TakeOver] Done: {settings.Mods.Count} SnakeBite mod(s) and {state.GameDirFiles.Count} game folder file(s) will be removed by the apply that follows");
        return state;
    }

    /// <summary>A reason the a_chunk7.dat/a_texture7.dat pair can't be trusted, or null. Sizes are SnakeBite's Checkc7t7Setup minimums.</summary>
    public static string? SplitArchivesProblem(GameLayout layout)
    {
        if (!File.Exists(layout.Chunk7Path) || !File.Exists(layout.Texture7Path))
            return "a_chunk7.dat or a_texture7.dat is missing.";
        if (new FileInfo(layout.Chunk7Path).Length < MinChunk7Size || new FileInfo(layout.Texture7Path).Length < MinTexture7Size)
            return "a_chunk7.dat or a_texture7.dat is smaller than SnakeBite setup makes it.";
        try
        {
            using QarArchive chunk7 = QarArchive.Open(layout.Chunk7Path);
            using QarArchive texture7 = QarArchive.Open(layout.Texture7Path);
            if (chunk7.Entries.Count == 0 || texture7.Entries.Count == 0)
                return "a_chunk7.dat or a_texture7.dat is empty.";
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            return $"a_chunk7.dat or a_texture7.dat can't be read ({e.Message}).";
        }
        return null;
    }

    // an empty or broken snakebite.xml (SnakeBite crashed while saving) must not block the take over: its records are lost
    private static SnakeBiteSettings LoadSettings(GameLayout layout)
    {
        if (!File.Exists(layout.SnakeBiteSettings))
            return new SnakeBiteSettings();
        try
        {
            return SnakeBiteSettings.Load(layout.SnakeBiteSettings);
        }
        catch (Exception e) when (e is InvalidOperationException or System.Xml.XmlException or InvalidDataException)
        {
            Log.Warn($"[TakeOver] Couldn't read snakebite.xml, so the files SnakeBite put in the game folder can't be found and are left where they are ({e.Message})");
            return new SnakeBiteSettings();
        }
    }

    private static List<QarEntryInfo> Baseline(QarArchive archive, QarDictionary dictionary, bool zeroArchive) =>
        archive.Entries.Where(e => zeroArchive
            ? dictionary.GzsName(e.Hash) == "foxpatch.dat"
            : dictionary.GzsName(e.Hash).Contains(".lua", StringComparison.Ordinal)).ToList();

    private static byte[]? OriginalFoxfs(GameLayout layout)
    {
        if (!File.Exists(layout.SnakeBiteChunk0Original))
            return null;
        try
        {
            using QarArchive original = QarArchive.Open(layout.SnakeBiteChunk0Original);
            byte[] foxfs = Foxfs.Read(original);
            return Foxfs.IsPrepared(foxfs) ? null : foxfs;
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            Log.Warn($"[TakeOver] Couldn't read chunk0.dat.original ({e.Message})");
            return null;
        }
    }

    private static List<(string Path, string? ContentHash)> GameDirFiles(SnakeBiteSettings settings) =>
        settings.Mods.SelectMany(m => m.ModFileEntries).Concat(settings.GameData.GameFileEntries)
            .Select(f => (Path: f.FilePath.Replace('\\', '/').TrimStart('/'), f.ContentHash))
            .Where(f => f.Path.Length > 0 && !ModFiles.IsIgnored(f.Path)) // never the game's own files, whatever snakebite.xml says
            .DistinctBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
