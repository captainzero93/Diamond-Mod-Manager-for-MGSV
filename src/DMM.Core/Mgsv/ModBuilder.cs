// Builds a .mgsv from a mod folder like MakeBite
using System.IO.Compression;
using DMM.Core.Fpk;
using DMM.Core.Hashing;
using DMM.Core.Logging;

namespace DMM.Core.Mgsv;

/// <summary>What the author types in: MakeBite's name, version, author, website, game version and description boxes.</summary>
public sealed record ModInfo(string Name, string Version, string Author, string Website, string GameVersion, string Description)
{
    public const string DefaultGameVersion = "1.0.15.4";

    /// <summary>MakeBite fills the boxes from metadata.xml in the folder; readme.txt replaces the description.</summary>
    public static ModInfo? FromFolder(string folder)
    {
        string xml = Path.Combine(folder, "metadata.xml");
        ModInfo? info = null;
        if (File.Exists(xml))
        {
            using FileStream stream = File.OpenRead(xml);
            ModEntry entry = ModEntry.Parse(stream);
            info = new ModInfo(entry.Name, entry.Version, entry.Author, entry.Website, entry.MGSVersion.Value, entry.Description.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        }
        string readme = Path.Combine(folder, "readme.txt");
        if (File.Exists(readme))
            info = (info ?? new ModInfo("", "", "", "", DefaultGameVersion, "")) with { Description = File.ReadAllText(readme) };
        return info;
    }
}

/// <summary>Everything a build will put in the .mgsv, worked out from the folder without writing anything.</summary>
public sealed class ModBuildPlan
{
    public sealed record Pack(string RelativePath, bool Fpkd, List<(string InFpkPath, string? SourceFile, byte[]? Data)> Files, string? FromPrebuilt);

    public required string SourceDir { get; init; }
    public List<Pack> Packs { get; } = [];
    /// <summary>QAR files: relative path in the folder and, for packs built here, the pack that supplies the bytes.</summary>
    public List<(string RelativePath, Pack? Built)> QarFiles { get; } = [];
    public List<string> GameDirFiles { get; } = [];
    public List<string> Videos { get; } = [];
    public List<string> Warnings { get; } = [];

    /// <summary>Files from the folder that go into the .mgsv (pack folder contents, prebuilt packs, QAR, video and GameDir files), sorted.</summary>
    public List<string> SourceFiles()
    {
        var files = new List<string>();
        files.AddRange(QarFiles.Where(q => q.Built == null).Select(q => q.RelativePath));
        foreach (Pack pack in Packs)
        {
            if (pack.FromPrebuilt != null)
                files.Add(pack.FromPrebuilt);
            else
                files.AddRange(pack.Files.Where(f => f.SourceFile != null).Select(f => System.IO.Path.GetRelativePath(SourceDir, f.SourceFile!).Replace('/', '\\')));
        }
        files.AddRange(Videos);
        files.AddRange(GameDirFiles);
        return files.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
    }
}

public sealed record ModBuildResult(int QarEntries, int FpkEntries, int FileEntries, int WmvEntries);

public static class ModBuilder
{
    /// <summary>MakeBite's AssemblyVersion, written as SBVersion; SnakeBite compares it with its own version.</summary>
    public const string MakeBiteVersion = "0.9.2.5";

    private const string ExternalDirName = "GameDir";
    private const string MovieDir = @"Assets\tpp\movie\Win";
    private static readonly string[] ArchiveFolders = ["_fpk", "_fpkd", "_pftx"];

    // Tools.ignoreFileList: game files a mod must not ship in GameDir (matched anywhere in the path, as MakeBite does)
    private static readonly string[] IgnoreFiles =
    [
        "mgsvtpp.exe", "mgsvmgo.exe", "steam_api64.dll", "steam_appid.txt", "version_info.txt", "chunk0.dat", "chunk1.dat", "chunk2.dat",
        "chunk3.dat", "texture0.dat", "texture1.dat", "texture2.dat", "texture3.dat", "texture4.dat", "00.dat", "01.dat", "snakebite.xml",
    ];

    // Build.vanillaWmvNames: a mod may not replace the game's own videos
    private static readonly ulong[] VanillaWmvHashes =
    [
        0xe2f9a1fda590d087, 0xe2f861abe2e17760, 0xe2fbebbd66f86086, 0xe2f867210cb635ca, 0xe2f8e499bc8f3606, 0xe2fb01787df277e4,
        0xe2faa449a7e0781d, 0xe2fb41f633494d0c, 0xe2fb02c35da41a21, 0xe2f986b5fa138174,
    ];

    // relative paths use backslashes, as MakeBite's string checks expect
    private static string Rel(string root, string path) => Path.GetRelativePath(root, path).Replace('/', '\\');

    // folder and name of a backslash relative path, the same on every platform
    private static string DirOf(string relative) => relative.LastIndexOf('\\') is int i and >= 0 ? relative[..i] : "";
    private static string NameOf(string relative) => relative[(relative.LastIndexOf('\\') + 1)..];

    public static ModBuildPlan Plan(string sourceDir, QarDictionary qar, FpkDictionary fpk)
    {
        string root = Path.GetFullPath(sourceDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"{root} doesn't exist");
        var plan = new ModBuildPlan { SourceDir = root };
        string[] allDirs = Directory.GetDirectories(root, "*", SearchOption.AllDirectories).Select(d => Rel(root, d)).Order(StringComparer.Ordinal).ToArray();
        var allDirSet = new HashSet<string>(allDirs, StringComparer.OrdinalIgnoreCase);

        // fpk files and the hashed-name check on them (formMain/BuildArchive)
        var fpkFiles = Directory.GetFiles(root, "*.fpk*", SearchOption.AllDirectories).Select(f => Rel(root, f)).Order(StringComparer.Ordinal).ToList();
        foreach (string fpkFile in fpkFiles.ToList())
        {
            if (!fpkFile.StartsWith("Assets", StringComparison.Ordinal) && qar.UpdateName(fpkFile) is { } updated && fpkFiles.Contains(FoxPath.ToWinPath(updated)))
                fpkFiles.Remove(fpkFile);
        }

        // pack folders: x_fpk / x_fpkd folders, plus prebuilt packs MakeBite extracts to a folder of that name when there is none
        var packSources = new List<(string Folder, string? Prebuilt)>();
        foreach (string dir in allDirs)
        {
            string name = dir[(dir.LastIndexOf('\\') + 1)..];
            if (name.Contains('_') && name[(name.LastIndexOf('_') + 1)..].ToLowerInvariant() is "fpk" or "fpkd")
                packSources.Add((dir, null));
        }
        foreach (string file in Directory.GetFiles(root, "*.fpk*", SearchOption.AllDirectories).Select(f => Rel(root, f)).Order(StringComparer.Ordinal))
        {
            if (file.EndsWith(".fpkl", StringComparison.Ordinal) || file.EndsWith(".xml", StringComparison.Ordinal))
                continue;
            string folderName = NameOf(file).Replace('.', '_');
            string folder = DirOf(file) is { Length: > 0 } parent ? parent + "\\" + folderName : folderName;
            if (allDirSet.Contains(folder) || !(folderName.EndsWith("_fpk", StringComparison.OrdinalIgnoreCase) || folderName.EndsWith("_fpkd", StringComparison.OrdinalIgnoreCase)))
                continue;
            packSources.Add((folder, file));
        }

        // hashed pack folder names that also exist unhashed are left out
        var folderNames = new HashSet<string>(packSources.Select(p => p.Folder), StringComparer.Ordinal);
        foreach ((string folder, string? prebuilt) in packSources.ToList())
        {
            if (folder.StartsWith("Assets", StringComparison.Ordinal) || qar.UpdateName(folder.Replace("_fpk", ".fpk")) is not { } updated)
                continue;
            string updatedWin = FoxPath.ToWinPath(updated);
            if (folderNames.Contains(updatedWin.Replace(".fpk", "_fpk")) || fpkFiles.Contains(updatedWin))
            {
                plan.Warnings.Add($"{NameOf(folder)} was not packed, because {NameOf(updatedWin)} (its unhashed name) is also in the folder.");
                packSources.Remove((folder, prebuilt));
            }
        }

        foreach ((string folder, string? prebuilt) in packSources)
        {
            string dest = folder.Replace("_fpk", ".fpk");
            bool fpkd = dest.EndsWith(".fpkd", StringComparison.OrdinalIgnoreCase);
            var files = new List<(string, string?, byte[]?)>();
            if (prebuilt == null)
            {
                string full = Path.Combine(root, folder.Replace('\\', Path.DirectorySeparatorChar));
                foreach (string file in Directory.GetFiles(full, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                {
                    string inFpk = "/" + Rel(full, file).Replace('\\', '/');
                    if (FpkLayout.IsExtensionValidForArchive(file, dest))
                        files.Add((inFpk, file, null));
                    else
                        plan.Warnings.Add($"{folder}\\{Rel(full, file)} is not a valid file for a {Path.GetExtension(dest)} archive; left out.");
                }
            }
            else
            {
                FpkArchive archive = FpkArchive.Read(File.ReadAllBytes(Path.Combine(root, prebuilt.Replace('\\', Path.DirectorySeparatorChar))), fpk);
                foreach (FpkEntry entry in archive.Entries)
                {
                    string inFpk = FoxPath.ToQarPath(entry.Name);
                    if (FpkLayout.IsExtensionValidForArchive(inFpk, dest))
                        files.Add((inFpk, null, entry.Data));
                    else
                        plan.Warnings.Add($"{prebuilt}: {inFpk} is not a valid file for a {Path.GetExtension(dest)} archive; left out.");
                }
            }
            plan.Packs.Add(new ModBuildPlan.Pack(dest, fpkd, files, prebuilt));
        }

        // QAR files: everything outside pack folders and GameDir with a .dat file type (or a video); built packs replace files of their name
        var built = plan.Packs.ToDictionary(p => p.RelativePath, StringComparer.OrdinalIgnoreCase);
        var qarFolders = allDirs.Where(d => !IsArchiveFolder(d) && !("\\" + d).Contains(ExternalDirName, StringComparison.Ordinal)).Append("").ToList();
        var qarFiles = new List<string>();
        foreach (string folder in qarFolders)
        {
            string full = folder.Length == 0 ? root : Path.Combine(root, folder.Replace('\\', Path.DirectorySeparatorChar));
            var names = Directory.GetFiles(full).Select(f => Rel(root, f)).ToList();
            names.AddRange(built.Keys.Where(k => string.Equals(DirOf(k), folder, StringComparison.OrdinalIgnoreCase)));
            foreach (string file in names.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
            {
                string fileName = "\\" + NameOf(file);
                if (!FpkLayout.IsExtensionValidForArchive(file, ".dat") && !fileName.Contains("wmv", StringComparison.Ordinal))
                {
                    if (!fileName.Contains("metadata.xml", StringComparison.Ordinal) && !fileName.Contains("readme.txt", StringComparison.Ordinal))
                        plan.Warnings.Add($"{file} is not a file type the .dat archives take; left out.");
                    continue;
                }
                if (!fileName.Contains("metadata.xml", StringComparison.Ordinal) && !fileName.Contains("readme.txt", StringComparison.Ordinal))
                    qarFiles.Add(file);
            }
        }
        foreach (string file in qarFiles.ToList())
        {
            if (file.StartsWith("Assets", StringComparison.Ordinal) || qar.UpdateName(file) is not { } updated)
                continue;
            if (qarFiles.Contains(FoxPath.ToWinPath(updated), StringComparer.Ordinal))
            {
                plan.Warnings.Add($"{NameOf(file)} was not added, because {NameOf(FoxPath.ToWinPath(updated))} (its unhashed name) is also in the folder.");
                qarFiles.Remove(file);
            }
        }
        foreach (string file in qarFiles)
        {
            if (!Hashable(FoxPath.ToQarPath(file)))
            {
                plan.Warnings.Add($"{file} has characters the game's path hash cannot take; left out.");
                continue;
            }
            if (("\\" + file).Contains(MovieDir, StringComparison.Ordinal))
            {
                if (VanillaWmvHashes.Contains(FoxPath.NameToHash(FoxPath.ToQarPath(file))))
                    plan.Warnings.Add($"{file} would replace one of the game's own videos; left out.");
                else
                    plan.Videos.Add(file);
            }
            else
                plan.QarFiles.Add((file, built.GetValueOrDefault(file)));
        }

        // game folder files: anything under a GameDir folder, except the game's own files
        foreach (string folder in allDirs.Where(d => !IsArchiveFolder(d) && ("\\" + d).Contains(ExternalDirName, StringComparison.Ordinal)))
        {
            string full = Path.Combine(root, folder.Replace('\\', Path.DirectorySeparatorChar));
            foreach (string file in Directory.GetFiles(full).Order(StringComparer.Ordinal))
            {
                if (IgnoreFiles.Any(i => file.Contains(i, StringComparison.Ordinal)))
                {
                    plan.Warnings.Add($"{Rel(root, file)} is a game file DMM and SnakeBite never install; left out.");
                    continue;
                }
                if (!Path.GetFileName(file).Contains("metadata.xml", StringComparison.Ordinal))
                    plan.GameDirFiles.Add(Rel(root, file));
            }
        }
        return plan;
    }

    private static bool IsArchiveFolder(string relativeDir) => ArchiveFolders.Any(a => ("\\" + relativeDir).Contains(a, StringComparison.Ordinal));

    /// <summary>
    /// Writes metadata.xml (details only, as MakeBite does) into the folder, then the .mgsv: packs built from their folders, QAR files,
    /// GameDir files, videos renamed to their hash, and the full metadata.xml.
    /// </summary>
    private static bool Hashable(string qarPath)
    {
        try
        {
            _ = FoxPath.NameToHash(qarPath);
            return true;
        }
        catch (Exception e) when (e is OverflowException or ArgumentException)
        {
            return false;
        }
    }

    public static ModBuildResult Build(ModBuildPlan plan, ModInfo info, string outputPath)
    {
        var meta = new MakeBiteModEntry
        {
            Name = info.Name,
            Version = info.Version,
            Author = info.Author,
            Website = info.Website,
            Description = info.Description,
            MGSVersion = new MakeBiteVersion { Version = info.GameVersion },
        };
        AtomicWrite(Path.Combine(plan.SourceDir, "metadata.xml"), meta.ToXml());
        meta.SBVersion = new MakeBiteVersion { Version = MakeBiteVersion };

        string temp = outputPath + ".NewBite_Build";
        try
        {
            WritePackage(plan, meta, temp);
        }
        catch
        {
            // a half-written package is never left beside the output
            try { File.Delete(temp); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Warn($"[Build] Couldn't delete the temporary file {temp} ({e.Message})"); }
            throw;
        }
        File.Move(temp, outputPath, overwrite: true);
        Log.Info($"[Build] {info.Name} {info.Version}: {meta.ModQarEntries.Count} QAR, {meta.ModFpkEntries.Count} pack, {meta.ModFileEntries.Count} game folder, " +
                 $"{meta.ModWmvEntries.Count} video entries -> {outputPath}");
        return new ModBuildResult(meta.ModQarEntries.Count, meta.ModFpkEntries.Count, meta.ModFileEntries.Count, meta.ModWmvEntries.Count);
    }

    private static void WritePackage(ModBuildPlan plan, MakeBiteModEntry meta, string temp)
    {
        using (FileStream stream = new(temp, FileMode.Create, FileAccess.Write))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var packBytes = new Dictionary<ModBuildPlan.Pack, byte[]>();
            foreach (ModBuildPlan.Pack pack in plan.Packs)
            {
                string fpkFile = FoxPath.ToQarPath(pack.RelativePath);
                var data = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                foreach ((string inFpk, string? source, byte[]? bytes) in pack.Files)
                {
                    byte[] content = bytes ?? File.ReadAllBytes(source!);
                    data[inFpk] = content;
                    meta.ModFpkEntries.Add(new MakeBiteFpkEntry { FilePath = inFpk, FpkFile = fpkFile, ContentHash = Md5Key.ToHex(Md5Key.Of(content)) });
                }
                // GzsLib.WriteFpkArchive: sorted, no references
                var archive = new FpkArchive { IsFpkd = pack.Fpkd };
                foreach (string name in FpkLayout.SortFpkFiles(pack.Fpkd, data.Keys.ToList()))
                    archive.Entries.Add(new FpkEntry { Name = name, Data = data[name], Md5 = [] });
                packBytes[pack] = archive.Write();
            }

            foreach ((string relative, ModBuildPlan.Pack? builtPack) in plan.QarFiles)
            {
                string qarPath = FoxPath.ToQarPath(relative);
                byte[] content = builtPack != null ? packBytes[builtPack] : File.ReadAllBytes(Full(plan, relative));
                Add(zip, QarZipPath(relative), content);
                meta.ModQarEntries.Add(new MakeBiteQarEntry
                {
                    FilePath = qarPath,
                    Compressed = relative.EndsWith(".fpk", StringComparison.Ordinal) || relative.EndsWith(".fpkd", StringComparison.Ordinal),
                    ContentHash = Md5Key.ToHex(Md5Key.Of(content)),
                    Hash = FoxPath.NameToHash(qarPath),
                });
            }

            foreach (string relative in plan.Videos)
            {
                ulong hash = FoxPath.NameToHash(FoxPath.ToQarPath(relative));
                string hexName = hash.ToString("x") + ".dat";
                string zipPath = VideoZipPath(relative);
                byte[] content = File.ReadAllBytes(Full(plan, relative));
                Add(zip, zipPath, content);
                meta.ModWmvEntries.Add(new MakeBiteWmvEntry { Hash = hash });
                meta.ModFileEntries.Add(new MakeBiteFileEntry { FilePath = "/master/" + hexName, ContentHash = Md5Key.ToHex(Md5Key.Of(content)) });
            }

            foreach (string relative in plan.GameDirFiles)
            {
                byte[] content = File.ReadAllBytes(Full(plan, relative));
                Add(zip, relative, content);
                string filePath = FoxPath.ToQarPath(relative);
                if (filePath.StartsWith("/" + ExternalDirName, StringComparison.Ordinal))
                    filePath = filePath[(ExternalDirName.Length + 1)..];
                meta.ModFileEntries.Add(new MakeBiteFileEntry { FilePath = filePath, ContentHash = Md5Key.ToHex(Md5Key.Of(content)) });
            }

            Add(zip, "metadata.xml", meta.ToXml());
        }
    }

    /// <summary>Path in the .mgsv of a QAR file.</summary>
    public static string QarZipPath(string relative) => FoxPath.ToWinPath(FoxPath.ToQarPath(relative));

    /// <summary>Path in the .mgsv of a video: GameDir\master\&lt;hash&gt;.dat, as MakeBite moves it.</summary>
    public static string VideoZipPath(string relative)
    {
        string moved = ("\\" + relative).Replace(MovieDir, ExternalDirName + "\\master").TrimStart('\\');
        string hexName = FoxPath.NameToHash(FoxPath.ToQarPath(relative)).ToString("x") + ".dat";
        return DirOf(moved) is { Length: > 0 } dir ? dir + "\\" + hexName : hexName;
    }

    private static string Full(ModBuildPlan plan, string relative) => Path.Combine(plan.SourceDir, relative.Replace('\\', Path.DirectorySeparatorChar));

    private static void Add(ZipArchive zip, string path, byte[] content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(path.Replace('\\', '/'), CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        stream.Write(content);
    }

    private static void AtomicWrite(string path, byte[] content)
    {
        string temp = path + ".NewBite_Build";
        File.WriteAllBytes(temp, content);
        File.Move(temp, path, overwrite: true);
    }
}
