// Returns the game to retail
using DMM.Core.Game;
using DMM.Core.Logging;
using DMM.Core.Mgsv;
using DMM.Core.State;

namespace DMM.Core.Engine;

/// <summary>
/// Begin applies with every mod off, which takes mod files out of 00.dat/01.dat/chunk0.dat and the game folder and puts back
/// game folder files DMM replaced. When retail copies (*.original) are present and check out, they are put back and the
/// restore is finished there. Otherwise Steam "Verify integrity of game files" restores 00.dat, 01.dat and chunk0.dat, and
/// Finish deletes a_chunk7.dat and a_texture7.dat, which Steam leaves behind. Either way DMM's folder is emptied but for its logs;
/// the files in the mods folder are not touched.
/// </summary>
public static class RetailRestore
{
    public const string SteamVerifyUrl = "steam://validate/287700";

    /// <summary>Returns true when the game is retail again (retail copies used), false when Steam verify is needed.</summary>
    public static bool Begin(GameLayout layout, ManagerState state, ModLibrary library, Lazy<Hashing.QarDictionary> qar, Lazy<Hashing.FpkDictionary> fpk)
    {
        // the committed state keeps which mods were on, even if the apply is interrupted and rolled forward later
        Applier.Apply(layout, state, library, new ApplyOptions { Force = true, NoMods = true }, qar, fpk);

        if (RetailCopies.Valid(layout, state, full: true))
        {
            var journal = new Journal(layout, "restore retail from copies");
            RetailCopies.Restore(layout, journal);
            Retire(layout, journal, state);
            journal.Run();
            RemoveLeftovers(layout);
            Log.Info("[Restore] Retail files put back from the retail copies; a_chunk7.dat and a_texture7.dat removed");
            return true;
        }

        state.AwaitingSteamVerify = true;
        StateStore.Save(layout.StateFile, state);
        Log.Info($"[Restore] Mods removed. Next: {layout.RetailFilesHint}, then finish the restore in DMM.");
        return false;
    }

    /// <summary>Returns a note on whether the restored files match the retail files DMM recorded at setup.</summary>
    public static string Finish(GameLayout layout, ManagerState state)
    {
        GameStatus status = GameInspector.Inspect(layout, state);
        if (status.State != GameFilesState.RetailWithState)
            throw new InvalidOperationException($"The game files aren't retail yet. {layout.RetailFilesHint} first.");
        // chunk0.dat back is not enough: while 00.dat or 01.dat is still DMM's, a_chunk7.dat/a_texture7.dat are needed
        foreach ((FileFingerprint? applied, string path) in new[] { (state.Applied?.Zero, layout.ZeroPath), (state.Applied?.One, layout.OnePath) })
            if (applied != null && (applied.QuickMatch(path) || applied.Matches(path)))
                throw new InvalidOperationException($"{Path.GetFileName(path)} is still the modded one, so Steam hasn't finished. {layout.RetailFilesHint} and let it finish, then try again.");

        string note = "DMM has no record of the retail files (SnakeBite set the game up), so it couldn't check them.";
        if (state.RetailZero != null && state.RetailOne != null && state.RetailChunk0 != null)
        {
            bool match = state.RetailZero.Matches(layout.ZeroPath) && state.RetailOne.Matches(layout.OnePath) && state.RetailChunk0.Matches(layout.Chunk0Path);
            note = match
                ? "00.dat, 01.dat and chunk0.dat match the retail files recorded before setup."
                : "00.dat, 01.dat or chunk0.dat aren't the same as before setup. A game update since then would explain it.";
        }

        var journal = new Journal(layout, "finish retail restore");
        // none after a restore DMM ran; when Steam replaced the files on its own, mod files are still in the game folder
        Applier.RemoveGameDirFiles(layout, state, _ => false, journal);
        journal.Delete(layout.Chunk7Path);
        journal.Delete(layout.Texture7Path);
        Retire(layout, journal, state);
        journal.Run();
        RemoveLeftovers(layout);
        Log.Info("[Restore] Finished: a_chunk7.dat and a_texture7.dat removed. " + note);
        return note;
    }

    /// <summary>
    /// Game files broken (a_chunk7.dat/a_texture7.dat or the baseline gone, so nothing can be applied) but the retail copies
    /// check out: mod files leave the game folder directly, the copies are put back and DMM's setup files are removed.
    /// </summary>
    public static void RestoreCopiesOverBroken(GameLayout layout, ManagerState state)
    {
        var journal = new Journal(layout, "restore retail from copies");
        Applier.RemoveGameDirFiles(layout, state, _ => false, journal);
        RetailCopies.Restore(layout, journal);
        Retire(layout, journal, state);
        journal.Run();
        RemoveLeftovers(layout);
        Log.Info("[Restore] Broken game files replaced with the retail copies; a_chunk7.dat and a_texture7.dat removed");
    }

    // the game is retail: DMM's records go, and snakebite.xml, which describes the modded files, goes too
    private static void Retire(GameLayout layout, Journal journal, ManagerState state)
    {
        state.AwaitingSteamVerify = false;
        journal.CommitState(state);
        journal.Delete(layout.BaselineDir);
        journal.Delete(layout.StateFile);
        journal.Delete(layout.SnakeBiteSettings);
    }

    /// <summary>
    /// Removes the rest of DMM's folder once the restore is done: library entries, staging, emptied backup folders, the
    /// snakebite.xml backup and state records of earlier versions, and the retail copies (*.original), which match the game
    /// files now and are made again at the next setup. Best effort, so a file held open elsewhere cannot fail a finished
    /// restore. The logs stay, and so does any backed-up file of the user's that was not put back.
    /// </summary>
    private static void RemoveLeftovers(GameLayout layout)
    {
        if (!Directory.Exists(layout.StateDir))
            return;
        IEnumerable<string> records = Directory.EnumerateFiles(layout.StateDir, "state.restored-*.json")
            .Concat(Directory.EnumerateFiles(layout.StateDir, "snakebite.xml.*")).ToList();
        foreach (string path in new[] { layout.LibraryDir, layout.StagingDir, layout.GameDirOriginalsDir }.Concat(records).Concat(RetailCopies.Paths(layout)))
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                else if (Directory.Exists(path) && !(path == layout.GameDirOriginalsDir && Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Any()))
                    AtomicFile.DeleteDirectory(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"[Restore] Couldn't remove {Path.GetFileName(path)} ({e.Message})");
            }
        }
    }
}
