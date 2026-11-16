// DMM state file model (D-Mod\state.json in the game folder)
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DMM.Core.State;

public sealed class ManagerState
{
    public const int CurrentFormat = 1;

    public int Format { get; set; } = CurrentFormat;

    /// <summary>How the baseline was made: "setup" (from retail) or "snakebite" (adopted from a SnakeBite install).</summary>
    public string BaselineSource { get; set; } = "";
    public DateTime BaselineCreatedUtc { get; set; }
    public string GameVersion { get; set; } = "";

    /// <summary>Retail file fingerprints recorded before setup, when DMM ran setup.</summary>
    public FileFingerprint? RetailZero { get; set; }
    public FileFingerprint? RetailOne { get; set; }
    public FileFingerprint? RetailChunk0 { get; set; }

    /// <summary>Load order: first is installed first, later mods win.</summary>
    public List<LibraryModRef> Mods { get; set; } = [];

    /// <summary>What the last apply wrote. Null before the first apply.</summary>
    public AppliedState? Applied { get; set; }

    /// <summary>Game folder files DMM manages, by path relative to the game folder with forward slashes, case-insensitive.</summary>
    public Dictionary<string, ManagedFile> GameDirFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Set after a restore to retail until Steam has verified the game files.</summary>
    public bool AwaitingSteamVerify { get; set; }
}

public sealed class LibraryModRef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string SourceFileName { get; set; } = "";
}

public sealed class AppliedState
{
    public DateTime Utc { get; set; }
    public FileFingerprint Zero { get; set; } = new();
    public FileFingerprint One { get; set; } = new();
    public FileFingerprint Chunk0 { get; set; } = new();
    public string FoxfsMd5 { get; set; } = "";
    public List<string> ModIds { get; set; } = [];
    /// <summary>Library package MD5 of each applied mod, so an updated package shows as not applied.</summary>
    public Dictionary<string, string> ModPackages { get; set; } = new(StringComparer.Ordinal);
    /// <summary>How each merged pack in 00.dat was made, by pack path.</summary>
    public Dictionary<string, Engine.PackRecord> Packs { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ManagedFile
{
    /// <summary>Library id of the mod that owns the file, or "snakebite" for files adopted from a SnakeBite install.</summary>
    public string Owner { get; set; } = "";
    public string Md5 { get; set; } = "";
    /// <summary>A file was at this path before DMM wrote it; the copy is in gamedir-originals.</summary>
    public bool HasOriginal { get; set; }
}

public sealed class FileFingerprint
{
    public long Size { get; set; }
    public string Md5 { get; set; } = "";
    public DateTime LastWriteUtc { get; set; }

    public static FileFingerprint Of(string path)
    {
        var info = new FileInfo(path);
        return new FileFingerprint { Size = info.Length, LastWriteUtc = info.LastWriteTimeUtc, Md5 = Hashing.Md5Key.ToHex(Hashing.Md5Key.OfFile(path)) };
    }

    /// <summary>Size and timestamp match; cheap check before hashing.</summary>
    public bool QuickMatch(string path)
    {
        var info = new FileInfo(path);
        return info.Exists && info.Length == Size && info.LastWriteTimeUtc == LastWriteUtc;
    }

    public bool Matches(string path) => File.Exists(path) && new FileInfo(path).Length == Size && Hashing.Md5Key.ToHex(Hashing.Md5Key.OfFile(path)) == Md5;
}

[JsonSerializable(typeof(ManagerState))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class StateJsonContext : JsonSerializerContext;

public static class StateStore
{
    public static ManagerState? Load(string path)
    {
        if (!File.Exists(path))
            return null;
        ManagerState state = JsonSerializer.Deserialize(File.ReadAllText(path), StateJsonContext.Default.ManagerState)
            ?? throw new InvalidDataException($"{path} is empty");
        if (state.Format > ManagerState.CurrentFormat)
            throw new InvalidDataException($"{path} was saved by a newer version of DMM. Update DMM to use it. (format {state.Format})");
        state.GameDirFiles = new Dictionary<string, ManagedFile>(state.GameDirFiles, StringComparer.OrdinalIgnoreCase);
        return state;
    }

    /// <summary>Crash-safe write (AtomicFile).</summary>
    public static void Save(string path, ManagerState state)
    {
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(state, StateJsonContext.Default.ManagerState));
    }
}
