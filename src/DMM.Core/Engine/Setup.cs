// First-time game file preparation, same layout as SnakeBite
using System.Diagnostics;
using DMM.Core.Fpk;
using DMM.Core.Game;
using DMM.Core.Hashing;
using DMM.Core.Logging;
using DMM.Core.Qar;
using DMM.Core.State;

namespace DMM.Core.Engine;

/// <summary>
/// SnakeBite ModManager.MoveDatFilesClean + ModifyFoxfs:
/// - 00.dat lua files go to 01.dat, everything else except foxpatch.dat goes to a_chunk7.dat (decoded and re-encoded as GzsTool does);
/// - the retail 01.dat is copied to a_texture7.dat;
/// - 00.dat keeps only foxpatch.dat;
/// - foxfs.dat in chunk0.dat gets the a_chunk7 chunk list (AddChunks).
/// Differences: entries keep their original hash and entries with an unknown extension are kept (SnakeBite drops "_unknown" names);
/// chunk0.dat entries other than foxfs.dat are copied unchanged instead of being decoded and re-encoded.
/// </summary>
public static class Setup
{
    public static void Run(GameLayout layout, ManagerState? previous, QarDictionary dictionary, bool keepRetailCopies)
    {
        var timer = Stopwatch.StartNew();
        Log.Info("[Setup] Preparing retail game files");
        CheckFreeSpace(layout, keepRetailCopies);

        FileFingerprint retailZero = null!, retailOne = null!, retailChunk0 = null!;
        Parallel.Invoke(
            () => retailZero = FileFingerprint.Of(layout.ZeroPath),
            () => retailOne = FileFingerprint.Of(layout.OnePath),
            () => retailChunk0 = FileFingerprint.Of(layout.Chunk0Path));
        Log.Info($"[Setup] Retail MD5: 00.dat {retailZero.Md5}, 01.dat {retailOne.Md5}, chunk0.dat {retailChunk0.Md5} at {timer.ElapsedMilliseconds} ms");
        if (retailZero.Md5 + retailOne.Md5 != TakeOver.RetailDatHash1_0_15_4)
            Log.Warn("[Setup] 00.dat and 01.dat aren't the retail 1.0.15.4 files SnakeBite expects. The game may be a different version or already modded (MD5 check against SnakeBite's vanillaDatHash)");
        // check every input before the retail copies are replaced: a half-finished Steam verify must not overwrite good copies
        if (previous?.Applied is { } applied && (applied.Zero.Md5 == retailZero.Md5 || applied.One.Md5 == retailOne.Md5))
            throw new InvalidDataException($"00.dat or 01.dat is still the modded one DMM wrote, not retail. Maybe Steam didn't finish a restore? {layout.RetailFilesHint}, then set up.");
        using (QarArchive chunk0 = QarArchive.Open(layout.Chunk0Path))
            if (Foxfs.IsPrepared(Foxfs.Read(chunk0)))
                throw new InvalidOperationException("The game files are already set up for mods, so they aren't retail (foxfs.dat lists a_chunk7.dat)");
        // retail copies that check out outrank a 00.dat/01.dat that differs from them, which is still modded (a SnakeBite install,
        // a half-finished verify, no DMM record to compare with); only a game update changes both, and then the copies go first
        string[] copies = RetailCopies.Paths(layout);
        if (File.Exists(copies[0]) && File.Exists(copies[1]))
        {
            string copyZero = "", copyOne = "";
            Parallel.Invoke(() => copyZero = Md5Key.ToHex(Md5Key.OfFile(copies[0])), () => copyOne = Md5Key.ToHex(Md5Key.OfFile(copies[1])));
            bool copiesRetail = copyZero + copyOne == TakeOver.RetailDatHash1_0_15_4 || (copyZero == previous?.RetailZero?.Md5 && copyOne == previous.RetailOne?.Md5);
            if (copiesRetail && (copyZero != retailZero.Md5 || copyOne != retailOne.Md5))
                throw new InvalidDataException($"00.dat or 01.dat differs from its retail copy (*.original), so it's still modded. {layout.RetailFilesHint}, then set up. " +
                                               "If the game was updated since the copies were made, delete 00.dat.original and 01.dat.original first.");
        }

        string zeroBuild = GameLayout.BuildPath(layout.ZeroPath);
        string oneBuild = GameLayout.BuildPath(layout.OnePath);
        string chunk7Build = GameLayout.BuildPath(layout.Chunk7Path);
        string texture7Build = GameLayout.BuildPath(layout.Texture7Path);
        string chunk0Build = GameLayout.BuildPath(layout.Chunk0Path);
        byte[] retailFoxfs, preparedFoxfs;

        using (QarArchive zero = QarArchive.Open(layout.ZeroPath))
        {
            var chunk7 = new List<QarWriteItem>();
            var one = new List<QarWriteItem>();
            var zeroOut = new List<QarWriteItem>();
            int renamed = 0, unknown = 0;
            foreach (QarEntryInfo entry in zero.Entries)
            {
                string name = dictionary.GzsName(entry.Hash);
                bool compress = FpkLayout.LastExtension(name) is "fpk" or "fpkd";
                var item = new QarWriteItem(entry.Hash, name, new DecodedQarSource(zero, entry, compress));
                if (name.EndsWith("_unknown", StringComparison.Ordinal))
                    unknown++;
                else if (FoxPath.NameToHash(name) != entry.Hash)
                    renamed++;

                if (name == "foxpatch.dat")
                    zeroOut.Add(item);
                else if (name.Contains(".lua", StringComparison.Ordinal))
                    one.Add(item);
                else
                    chunk7.Add(item);
            }
            if (zeroOut.Count != 1)
                throw new InvalidDataException($"00.dat has {zeroOut.Count} copies of foxpatch.dat instead of 1. Is this the retail 00.dat?");
            if (chunk7.Count == 0)
                throw new InvalidDataException($"00.dat holds only foxpatch.dat, so it's already set up for mods, not retail. Maybe Steam didn't finish a restore? {layout.RetailFilesHint}, then set up.");
            if (unknown > 0)
                Log.Warn($"[Setup] {unknown} files in 00.dat have an unknown file type. DMM keeps them in a_chunk7.dat (SnakeBite drops them)");
            if (renamed > 0)
                Log.Warn($"[Setup] {renamed} files in 00.dat don't match their name in the dictionary, so they keep their original hash");
            if (keepRetailCopies)
                RetailCopies.Make(layout, [retailZero, retailOne, retailChunk0]);

            LogWrite("a_chunk7.dat", QarWriter.Write(chunk7Build, GameLayout.Chunk7Flags, chunk7), timer);
            LogWrite("01.dat", QarWriter.Write(oneBuild, GameLayout.OneFlags, one), timer);
            LogWrite("00.dat", QarWriter.Write(zeroBuild, GameLayout.ZeroFlags, zeroOut), timer);
        }

        AtomicFile.CopyDurable(layout.OnePath, texture7Build);
        Log.Info($"[Setup] a_texture7.dat copied from retail 01.dat at {timer.ElapsedMilliseconds} ms");

        using (QarArchive chunk0 = QarArchive.Open(layout.Chunk0Path))
        {
            retailFoxfs = Foxfs.Read(chunk0);
            if (Foxfs.IsPrepared(retailFoxfs))
                throw new InvalidOperationException("The game files are already set up for mods, so they aren't retail (foxfs.dat lists a_chunk7.dat)");
            preparedFoxfs = Foxfs.AddChunks(retailFoxfs);
            if (chunk0.Flags != GameLayout.Chunk0Flags)
                Log.Warn($"[Setup] chunk0.dat has different header flags from the ones SnakeBite writes, so DMM keeps the retail ones ({chunk0.Flags}, SnakeBite writes {GameLayout.Chunk0Flags})");
            LogWrite("chunk0.dat", QarWriter.Write(chunk0Build, chunk0.Flags, Applier.Chunk0Items(chunk0, preparedFoxfs)), timer);
        }

        Directory.CreateDirectory(layout.BaselineDir);
        AtomicFile.CopyDurable(zeroBuild, GameLayout.BuildPath(layout.BaselineZero));
        AtomicFile.CopyDurable(oneBuild, GameLayout.BuildPath(layout.BaselineOne));
        AtomicFile.WriteAllBytes(layout.RetailFoxfs, retailFoxfs);
        AtomicFile.WriteAllBytes(GameLayout.BuildPath(layout.BaselineFoxfs), preparedFoxfs);

        ManagerState state = previous ?? new ManagerState();
        state.BaselineSource = "setup";
        state.BaselineCreatedUtc = DateTime.UtcNow;
        state.GameVersion = layout.GameVersion().ToString();
        state.RetailZero = retailZero;
        state.RetailOne = retailOne;
        state.RetailChunk0 = retailChunk0;
        state.Applied = new AppliedState
        {
            Utc = DateTime.UtcNow,
            Zero = FileFingerprint.Of(zeroBuild),
            One = FileFingerprint.Of(oneBuild),
            Chunk0 = new FileFingerprint { Size = new FileInfo(chunk0Build).Length, LastWriteUtc = File.GetLastWriteTimeUtc(chunk0Build) },
            FoxfsMd5 = Md5Key.ToHex(Md5Key.Of(preparedFoxfs)),
        };
        state.AwaitingSteamVerify = false;

        var journal = new Journal(layout, "setup");
        journal.Replace(GameLayout.BuildPath(layout.BaselineZero), layout.BaselineZero);
        journal.Replace(GameLayout.BuildPath(layout.BaselineOne), layout.BaselineOne);
        journal.Replace(GameLayout.BuildPath(layout.BaselineFoxfs), layout.BaselineFoxfs);
        journal.Replace(chunk7Build, layout.Chunk7Path);
        journal.Replace(texture7Build, layout.Texture7Path);
        journal.Replace(oneBuild, layout.OnePath);
        journal.Replace(zeroBuild, layout.ZeroPath);
        journal.Replace(chunk0Build, layout.Chunk0Path);
        journal.CommitState(state);
        journal.SyncSnakeBite();
        journal.Run();
        Log.Info($"[Setup] Done in {timer.ElapsedMilliseconds} ms");
    }

    private static void LogWrite(string name, QarWriteResult result, Stopwatch timer) =>
        Log.Info($"[Setup] {name}: {result.Reused} entries copied, {result.Encoded} re-encoded, {result.Length / 1024:n0} KB, at {timer.ElapsedMilliseconds} ms");

    private static void CheckFreeSpace(GameLayout layout, bool keepRetailCopies)
    {
        long retail = new FileInfo(layout.ZeroPath).Length + new FileInfo(layout.OnePath).Length + new FileInfo(layout.Chunk0Path).Length;
        long needed = new FileInfo(layout.ZeroPath).Length * 2 + new FileInfo(layout.OnePath).Length * 2 + new FileInfo(layout.Chunk0Path).Length + (256L << 20)
            + (keepRetailCopies ? retail - RetailCopies.Size(layout) : 0);
        string? root = Path.GetPathRoot(layout.GameDir);
        if (string.IsNullOrEmpty(root))
            return;
        var drive = new DriveInfo(root);
        if (drive.IsReady && drive.AvailableFreeSpace < needed)
            throw new IOException($"Setup needs about {needed / (1 << 20):n0} MB free on {root}, but there's only {drive.AvailableFreeSpace / (1 << 20):n0} MB.");
    }
}
