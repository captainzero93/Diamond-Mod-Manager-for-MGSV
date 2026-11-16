// Retail copies of 00.dat, 01.dat and chunk0.dat
using DMM.Core.Game;
using DMM.Core.Hashing;
using DMM.Core.Logging;
using DMM.Core.Qar;
using DMM.Core.State;

namespace DMM.Core.Engine;

/// <summary>Same file names as SnakeBite's backups (00.dat.original, 01.dat.original, chunk0.dat.original), so either tool can use them.</summary>
public static class RetailCopies
{
    public static string[] Paths(GameLayout layout) => [layout.ZeroPath + ".original", layout.OnePath + ".original", layout.SnakeBiteChunk0Original];

    public static long Size(GameLayout layout) => Paths(layout).Where(File.Exists).Sum(p => new FileInfo(p).Length);

    /// <summary>
    /// The copies exist and look retail: chunk0's foxfs.dat has no a_chunk7 line. With full, every copy's MD5 must match:
    /// 00/01 against retail 1.0.15.4 or the fingerprints recorded at setup, chunk0 against the fingerprint recorded at setup.
    /// Without a recorded chunk0 fingerprint (an install taken over from SnakeBite) the copies are not trusted.
    /// </summary>
    public static bool Valid(GameLayout layout, ManagerState? state, bool full = false)
    {
        string[] paths = Paths(layout);
        if (!paths.All(File.Exists))
            return false;
        try
        {
            using QarArchive chunk0 = QarArchive.Open(paths[2]);
            if (Foxfs.IsPrepared(Foxfs.Read(chunk0)))
                return false;
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            return false;
        }
        if (!full)
            return true;

        string zeroMd5 = "", oneMd5 = "", chunk0Md5 = "";
        Parallel.Invoke(
            () => zeroMd5 = Md5Key.ToHex(Md5Key.OfFile(paths[0])),
            () => oneMd5 = Md5Key.ToHex(Md5Key.OfFile(paths[1])),
            () => chunk0Md5 = Md5Key.ToHex(Md5Key.OfFile(paths[2])));
        bool known = zeroMd5 + oneMd5 == TakeOver.RetailDatHash1_0_15_4;
        bool recorded = state?.RetailZero?.Md5 == zeroMd5 && state.RetailOne?.Md5 == oneMd5;
        if (!known && !recorded)
        {
            Log.Warn($"[Retail] 00.dat.original or 01.dat.original isn't a retail 1.0.15.4 file or the one DMM recorded at setup (MD5 {zeroMd5}{oneMd5})");
            return false;
        }
        if (state?.RetailChunk0 == null)
        {
            Log.Warn($"[Retail] Can't check chunk0.dat.original: DMM didn't set this game up, so it has no record of the retail one (MD5 {chunk0Md5})");
            return false;
        }
        if (state.RetailChunk0.Md5 != chunk0Md5)
        {
            Log.Warn($"[Retail] chunk0.dat.original isn't the retail chunk0.dat DMM recorded at setup (MD5 {chunk0Md5}, expected {state.RetailChunk0.Md5})");
            return false;
        }
        return true;
    }

    /// <summary>Copies the current (retail) files to *.original. An existing copy is kept only when it is identical to the retail file.</summary>
    public static void Make(GameLayout layout, FileFingerprint[] retail)
    {
        string[] sources = [layout.ZeroPath, layout.OnePath, layout.Chunk0Path];
        string[] targets = Paths(layout);
        for (int i = 0; i < sources.Length; i++)
        {
            if (retail[i].Matches(targets[i]))
                continue;
            if (File.Exists(targets[i]))
                Log.Warn($"[Retail] {Path.GetFileName(targets[i])} wasn't the retail file, so it was replaced");
            string temp = GameLayout.BuildPath(targets[i]);
            AtomicFile.CopyDurable(sources[i], temp);
            if (File.Exists(targets[i]))
                File.SetAttributes(targets[i], FileAttributes.Normal); // a read-only SnakeBite .original
            File.Move(temp, targets[i], overwrite: true);
        }
        Log.Info("[Retail] Retail copies made: 00.dat.original, 01.dat.original, chunk0.dat.original");
    }

    /// <summary>Puts the retail files back and removes a_chunk7.dat and a_texture7.dat, through the journal.</summary>
    public static void Restore(GameLayout layout, Journal journal)
    {
        string[] targets = [layout.ZeroPath, layout.OnePath, layout.Chunk0Path];
        string[] copies = Paths(layout);
        for (int i = 0; i < targets.Length; i++)
        {
            string build = GameLayout.BuildPath(targets[i]);
            AtomicFile.CopyDurable(copies[i], build);
            journal.Replace(build, targets[i]);
        }
        journal.Delete(layout.Chunk7Path);
        journal.Delete(layout.Texture7Path);
    }
}
