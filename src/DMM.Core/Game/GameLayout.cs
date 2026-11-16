// MGSV TPP install folder layout and archive constants
using System.Diagnostics;

namespace DMM.Core.Game;

public sealed class GameLayout
{
    // GAMEVERSION: QAR flags SnakeBite writes (GzsLib.cs), game 1.0.15.4
    public const uint ZeroFlags = 3150304;
    public const uint OneFlags = 3150048;
    public const uint Chunk0Flags = 3146208;
    public const uint Chunk7Flags = 3146208;
    public const int SteamAppId = 287700;

    /// <summary>Folder in the game folder holding DMM's state; builds before 0.9.3 used LegacyStateFolder.</summary>
    public const string StateFolder = "D-Mod", LegacyStateFolder = "NewBite"; // D-Mod was DMM's earlier name; the folder keeps it so installs carry on

    private readonly string stateFolder;

    public string GameDir { get; }

    public GameLayout(string gameDir, string stateFolder = StateFolder)
    {
        GameDir = Path.GetFullPath(gameDir);
        this.stateFolder = stateFolder;
    }

    public string Exe => Path.Combine(GameDir, "mgsvtpp.exe");
    public string MasterDir => Path.Combine(GameDir, "master");
    public string ZeroPath => Path.Combine(MasterDir, "0", "00.dat");
    public string OnePath => Path.Combine(MasterDir, "0", "01.dat");
    public string Chunk0Path => Path.Combine(MasterDir, "chunk0.dat");
    public string Chunk7Path => Path.Combine(MasterDir, "a_chunk7.dat");
    public string Texture7Path => Path.Combine(MasterDir, "a_texture7.dat");
    public string SnakeBiteSettings => Path.Combine(GameDir, "snakebite.xml");
    public string SnakeBiteChunk0Original => Chunk0Path + ".original";

    public string StateDir => Path.Combine(GameDir, stateFolder);
    public string LegacyStateDir => Path.Combine(GameDir, LegacyStateFolder);
    public string StateFile => Path.Combine(StateDir, "state.json");
    public string JournalFile => Path.Combine(StateDir, "journal.json");
    public string LockFile => Path.Combine(StateDir, "dmod.lock");
    public string LibraryDir => Path.Combine(StateDir, "library");
    public string BaselineDir => Path.Combine(StateDir, "baseline");
    public string BaselineZero => Path.Combine(BaselineDir, "00.dat");
    public string BaselineOne => Path.Combine(BaselineDir, "01.dat");
    public string BaselineFoxfs => Path.Combine(BaselineDir, "foxfs.dat");
    public string RetailFoxfs => Path.Combine(BaselineDir, "foxfs.retail.dat");
    public string GameDirOriginalsDir => Path.Combine(StateDir, "gamedir-originals");
    public string StagingDir => Path.Combine(StateDir, "staging");
    public string LogDir => Path.Combine(StateDir, "logs");

    /// <summary>Temporary build file next to its target, so the final move stays on one volume.</summary>
    public static string BuildPath(string target) => target + ".NewBite_Build";

    /// <summary>
    /// Base archives searched for vanilla packs, in SnakeBite GzsLib.ReadBaseData priority order.
    /// </summary>
    public IEnumerable<string> BaseArchives =>
        new[] { "a_chunk7.dat", "data1.dat", "chunk0.dat", "chunk1.dat", "chunk2.dat", "chunk3.dat", "chunk4.dat", "chunk5_mgo0.dat", "chunk6_gzs0.dat" }
            .Select(name => Path.Combine(MasterDir, name));

    /// <summary>Installed by Steam: the folder sits in steamapps\common next to appmanifest_287700.acf.</summary>
    public bool IsSteamInstall
    {
        get
        {
            DirectoryInfo? common = Directory.GetParent(GameDir);
            string? steamapps = common?.Parent?.FullName;
            return common != null && steamapps != null && common.Name.Equals("common", StringComparison.OrdinalIgnoreCase)
                   && File.Exists(Path.Combine(steamapps, $"appmanifest_{SteamAppId}.acf"));
        }
    }

    /// <summary>How the retail 00.dat, 01.dat and chunk0.dat come back when DMM has no checked copies.</summary>
    public string RetailFilesHint => IsSteamInstall
        ? "Verify the game files in Steam"
        : @"Put back your own copies of master\0\00.dat, master\0\01.dat and master\chunk0.dat (or reinstall the game)";

    public bool LooksLikeGame => File.Exists(Exe) && File.Exists(ZeroPath) && File.Exists(OnePath) && File.Exists(Chunk0Path);

    public Version GameVersion()
    {
        FileVersionInfo info = FileVersionInfo.GetVersionInfo(Exe);
        return info.ProductVersion != null && Version.TryParse(info.ProductVersion, out Version? version) ? version : new Version(0, 0, 0, 0);
    }

    /// <summary>SnakeBite SettingsManager.IsUpToDate: same version, or 0.0.0.0, 1.0.15.2, 1.0.15.3.</summary>
    public static bool ModVersionOk(Version gameVersion, Version modVersion) =>
        gameVersion == modVersion || modVersion == new Version(0, 0, 0, 0) || modVersion == new Version(1, 0, 15, 2) || modVersion == new Version(1, 0, 15, 3);

    public static bool IsGameRunning()
    {
        Process[] found = Process.GetProcessesByName("mgsvtpp");
        foreach (Process process in found)
            process.Dispose();
        return found.Length > 0;
    }

    /// <summary>Full path of a game folder file from a mod's FileEntry path, or null if it would leave the game folder or touch DMM's folder.</summary>
    public string? GameDirTarget(string filePath)
    {
        string relative = filePath.Replace('\\', '/').TrimStart('/');
        if (relative.Length == 0)
            return null;
        string full = Path.GetFullPath(Path.Combine(GameDir, relative));
        string root = GameDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string state = StateDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string legacy = LegacyStateDir + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || full.StartsWith(state, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(legacy, StringComparison.OrdinalIgnoreCase))
            return null;
        return full;
    }
}
