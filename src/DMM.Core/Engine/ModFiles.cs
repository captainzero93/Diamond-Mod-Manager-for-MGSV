// What a mod's metadata.xml installs, after SnakeBite's renames and filters
using DMM.Core.Fpk;
using DMM.Core.Hashing;
using DMM.Core.Mgsv;

namespace DMM.Core.Engine;

/// <summary>A QAR file of a mod. QarPath is after the qar_dictionary rename; ToOne means it goes to 01.dat.</summary>
public sealed record ModQarFile(string QarPath, string OriginalPath, bool IsPack, bool ToOne)
{
    public ulong Hash => FoxPath.NameToHash(QarPath);
}

public sealed record ModFileSet(
    List<ModQarFile> Qar,
    /// <summary>Pack QAR path to the files the mod's FpkEntries list for it ("Assets\..." form).</summary>
    Dictionary<string, List<string>> PackFiles,
    /// <summary>Game folder paths, relative, forward slashes.</summary>
    List<string> GameDir,
    List<string> Warnings);

public static class ModFiles
{
    // SnakeBite Tools.ignoreFileList, matched with string.Contains on the file path
    private static readonly string[] IgnoreFiles =
    [
        "mgsvtpp.exe", "mgsvmgo.exe", "steam_api64.dll", "steam_appid.txt", "version_info.txt", "chunk0.dat", "chunk1.dat",
        "chunk2.dat", "chunk3.dat", "texture0.dat", "texture1.dat", "texture2.dat", "texture3.dat", "texture4.dat", "00.dat",
        "01.dat", "snakebite.xml",
    ];

    // archives DMM writes or the setup moves files into; SnakeBite's list predates them
    private static readonly string[] ManagedArchives = ["a_chunk7.dat", "a_texture7.dat", "foxfs.dat"];

    /// <summary>
    /// SnakeBite AddToSettingsFpk / InstallMods / ValidateModEntries / InstallGameDirFiles:
    /// non-/Assets/ QAR paths renamed through qar_dictionary.txt (HashingExtended.UpdateName), files that are not a .dat type
    /// skipped (paths containing "wmv" excepted), paths containing ".ftex" routed to 01.dat, game folder files on SnakeBite's
    /// ignore list skipped. Game folder paths that would leave the game folder are skipped too.
    /// </summary>
    public static ModFileSet Read(ModEntry meta, QarDictionary dictionary)
    {
        var warnings = new List<string>();
        var renames = new Dictionary<string, string>(StringComparer.Ordinal);
        var qar = new List<ModQarFile>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ModQarEntry entry in meta.ModQarEntries)
        {
            if (string.IsNullOrEmpty(entry.FilePath))
                continue;
            string originalPath = FoxPath.ToQarPath(entry.FilePath);
            string qarPath = originalPath;
            if (!entry.FilePath.StartsWith("/Assets/", StringComparison.Ordinal) && dictionary.UpdateName(entry.FilePath) is { } updated)
            {
                qarPath = FoxPath.ToQarPath(updated);
                renames[originalPath] = qarPath;
            }
            if (!FpkLayout.IsExtensionValidForArchive(qarPath, ".dat") && !qarPath.Contains("wmv", StringComparison.Ordinal))
            {
                warnings.Add($"{meta.Name}: skipped {qarPath}, it isn't a file type the .dat files take");
                continue;
            }
            if (!seen.Add(qarPath))
                continue; // listed twice, or two spellings of one path
            try
            {
                _ = FoxPath.NameToHash(qarPath);
            }
            catch (Exception e) when (e is OverflowException or ArgumentException)
            {
                warnings.Add($"{meta.Name}: skipped {qarPath}, its name doesn't work with the game's path hash ({e.Message})");
                continue;
            }
            qar.Add(new ModQarFile(qarPath, originalPath, FpkLayout.LastExtension(qarPath) is "fpk" or "fpkd", qarPath.Contains(".ftex", StringComparison.Ordinal)));
        }

        var packFiles = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (ModFpkEntry fpk in meta.ModFpkEntries)
        {
            // SnakeBite drops entries whose type the pack cannot hold; a pack left with none is installed whole, not merged
            if (string.IsNullOrEmpty(fpk.FpkFile) || string.IsNullOrEmpty(fpk.FilePath) || !FpkLayout.IsExtensionValidForArchive(fpk.FilePath, fpk.FpkFile))
                continue;
            string pack = FoxPath.ToQarPath(fpk.FpkFile);
            pack = renames.TryGetValue(pack, out string? renamed) ? renamed : pack;
            if (!packFiles.TryGetValue(pack, out List<string>? list))
                packFiles[pack] = list = [];
            list.Add(FoxPath.ToWinPath(fpk.FilePath));
        }

        var gameDir = new List<string>();
        foreach (ModFileEntry entry in meta.ModFileEntries)
        {
            // case-insensitive, unlike SnakeBite: Windows paths are, so /MGSVTPP.EXE would replace the game exe
            if (IsIgnored(entry.FilePath))
            {
                warnings.Add($"{meta.Name}: skipped game folder file {entry.FilePath} (on SnakeBite's skip list)");
                continue;
            }
            string relative = entry.FilePath.Replace('\\', '/').TrimStart('/');
            if (!IsSafeRelative(relative))
            {
                warnings.Add($"{meta.Name}: skipped game folder file {entry.FilePath}, it points outside the game folder");
                continue;
            }
            gameDir.Add(relative);
        }
        return new ModFileSet(qar, packFiles, gameDir, warnings);
    }

    /// <summary>Game folder paths mods never own: SnakeBite's ignore list and the archives the setup manages.</summary>
    public static bool IsIgnored(string filePath) =>
        IgnoreFiles.Concat(ManagedArchives).Any(ignored => filePath.Contains(ignored, StringComparison.OrdinalIgnoreCase));

    /// <summary>Relative path with no empty, "." or ".." parts, no drive and not inside DMM's own folder.</summary>
    public static bool IsSafeRelative(string relative) =>
        relative.Length > 0
        && !relative.Split('/').Any(ModLibrary.UnsafePart)
        && !relative.Split('/')[0].Equals(Game.GameLayout.StateFolder, StringComparison.OrdinalIgnoreCase)
        && !relative.Split('/')[0].Equals(Game.GameLayout.LegacyStateFolder, StringComparison.OrdinalIgnoreCase);

    /// <summary>Zip name holding a QAR file's content: the renamed path when the package has it, else the original (SnakeBite moves the file).</summary>
    public static string ZipName(ModQarFile file, Func<string, bool> packageHas)
    {
        string renamed = MgsvPackage.QarFileName(file.QarPath);
        return packageHas(renamed) ? renamed : MgsvPackage.QarFileName(file.OriginalPath);
    }
}
