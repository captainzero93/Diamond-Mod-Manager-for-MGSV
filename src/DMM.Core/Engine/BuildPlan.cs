// Result of planning a rebuild
using DMM.Core.Qar;

namespace DMM.Core.Engine;

public sealed record GameDirFile(string RelativePath, string ModId, Mgsv.LibraryMod Mod, string ZipName, string Md5);

/// <summary>
/// How a pack in 00.dat was made: Key identifies the inputs (vanilla pack, mods in order and their pack files), Md5 the
/// result, Steps the mod ids merged in order. An unchanged key lets the next apply reuse the entry without merging again.
/// </summary>
public sealed class PackRecord
{
    public string Key { get; set; } = "";
    public string Md5 { get; set; } = "";
    public List<string> Steps { get; set; } = [];
}

public sealed class BuildPlan
{
    public required List<QarWriteItem> Zero { get; init; }
    public required List<QarWriteItem> One { get; init; }
    public required byte[] Foxfs { get; init; }
    public required List<ulong> WmvHashes { get; init; }
    /// <summary>Key: path relative to the game folder, forward slashes.</summary>
    public required Dictionary<string, GameDirFile> GameDir { get; init; }
    public required Dictionary<string, PackRecord> Packs { get; init; }
    public required List<string> Warnings { get; init; }
    public required List<string> ModIds { get; init; }
    public int MergedPacks { get; init; }
    public int ReusedPacks { get; init; }
}
