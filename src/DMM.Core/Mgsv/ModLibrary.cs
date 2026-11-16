// Mod library: per-mod metadata and file MD5s
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DMM.Core.Fpk;
using DMM.Core.Hashing;
using DMM.Core.Logging;
using DMM.Core.State;

namespace DMM.Core.Mgsv;

public sealed class LibraryManifest
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string SourceFileName { get; set; } = "";
    public string PackageMd5 { get; set; } = "";
    /// <summary>Last write time of the .mgsv or .zip it was added from.</summary>
    public DateTime SourceModifiedUtc { get; set; }
    /// <summary>Zip file name (forward slashes) to size and MD5 of the file.</summary>
    public Dictionary<string, LibraryFile> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The .mgsv the files are read from: the mods folder file, or mod.mgsv in the library folder for a .mgsv taken out of a .zip.
    /// Null for copies made before 0.9.45, which hold the files extracted under files\.</summary>
    public string? PackagePath { get; set; }
    public long PackageSize { get; set; }
    public DateTime PackageModifiedUtc { get; set; }
}

public sealed class LibraryFile
{
    public long Size { get; set; }
    public string Md5 { get; set; } = "";
}

[JsonSerializable(typeof(LibraryManifest))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class LibraryJsonContext : JsonSerializerContext;

/// <summary>A mod in the library: its metadata and file MD5s, and where its files are read from.</summary>
public sealed class LibraryMod
{
    public required string Id { get; init; }
    public required string Dir { get; init; }
    public required ModEntry Metadata { get; init; }
    public required LibraryManifest Manifest { get; init; }

    public string FilesDir => Path.Combine(Dir, "files");

    /// <summary>Full path of the .mgsv the files are read from; null for an extracted copy.</summary>
    public string? PackageFile => Manifest.PackagePath is { } path ? Path.Combine(Dir, path) : null;

    // packages opened for reading, one per reader at a time (a zip cannot be read by two threads); closed by Close
    private readonly ConcurrentBag<MgsvPackage> open = [];

    /// <summary>Date of the file it was added from; copies added before that was recorded use the date they were added.</summary>
    public DateTime SourceDate => Manifest.SourceModifiedUtc != default ? Manifest.SourceModifiedUtc : Directory.GetCreationTimeUtc(Dir);

    public bool HasFile(string zipName) => Manifest.Files.ContainsKey(MgsvPackage.NormalizeName(zipName));

    public UInt128 FileMd5(string zipName) => Md5Key.FromHex(Manifest.Files[MgsvPackage.NormalizeName(zipName)].Md5);

    public long FileSize(string zipName) => Manifest.Files[MgsvPackage.NormalizeName(zipName)].Size;

    public Stream OpenFile(string zipName)
    {
        string name = MgsvPackage.NormalizeName(zipName);
        if (!Manifest.Files.ContainsKey(name))
            throw new FileNotFoundException($"{Metadata.Name}: {name} isn't in the mod file");
        if (PackageFile is not { } packageFile)
            return File.OpenRead(Path.Combine(FilesDir, ModLibrary.DiskName(name)));
        MgsvPackage package = open.TryTake(out MgsvPackage? idle) ? idle : MgsvPackage.Open(packageFile);
        try
        {
            if (!package.Contains(name))
                throw new FileNotFoundException($"{Metadata.Name}: {name} isn't in {packageFile} any more. Add the mod again.");
            return new PackageEntryStream(package.OpenFile(name), () => open.Add(package));
        }
        catch
        {
            package.Dispose();
            throw;
        }
    }

    /// <summary>The whole file, checked against the size and MD5 recorded when the mod was added.</summary>
    public byte[] ReadFile(string zipName)
    {
        byte[] content = new byte[FileSize(zipName)];
        using (Stream input = OpenFile(zipName))
        {
            input.ReadExactly(content);
            if (input.ReadByte() != -1 || Md5Key.Of(content) != FileMd5(zipName))
                throw new IOException($"{Metadata.Name}: {zipName} changed since you added the mod. Add the mod again.");
        }
        return content;
    }

    /// <summary>Closes the packages opened for reading, so the mods folder files are not held open.</summary>
    public void Close()
    {
        while (open.TryTake(out MgsvPackage? package))
            package.Dispose();
    }

    // an entry stream that gives its package back for the next reader when done
    private sealed class PackageEntryStream(Stream inner, Action release) : Stream
    {
        private bool released;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !released)
            {
                released = true;
                inner.Dispose();
                release();
            }
            base.Dispose(disposing);
        }
    }
}

public sealed record AddModResult(LibraryMod Mod, bool Replaced, IReadOnlyList<string> Warnings)
{
    /// <summary>Its .mgsv was taken out of a .zip into the mods folder by this add (not found there already).</summary>
    public bool TakenOutOfZip { get; init; }
}

public sealed class ModLibrary(string libraryDir)
{
    public string Dir { get; } = libraryDir;

    /// <summary>The current mods folder, where a .mgsv is looked for when the folder it was added from is gone.</summary>
    public string? ModsFolder { get; set; }

    public static string MakeId(string modName)
    {
        var slug = new StringBuilder();
        foreach (char c in modName.ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
                slug.Append(c);
            else if (slug.Length > 0 && slug[^1] != '-')
                slug.Append('-');
            if (slug.Length >= 40)
                break;
        }
        string hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(modName)))[..8].ToLowerInvariant();
        return (slug.ToString().Trim('-') is { Length: > 0 } s ? s + "-" : "") + hash;
    }

    /// <summary>Zip name to a relative path inside files\. Rejects names that would leave the folder.</summary>
    public static string DiskName(string zipName)
    {
        string[] parts = MgsvPackage.NormalizeName(zipName).Split('/');
        if (parts.Any(UnsafePart))
            throw new InvalidDataException($"The mod has a file name that isn't safe to use: {zipName}");
        return Path.Combine(parts);
    }

    private static readonly string[] DeviceNames = ["CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    /// <summary>A path part Windows would not store as written: empty, . or .., a drive or stream colon, a trailing dot or space, a device name.</summary>
    public static bool UnsafePart(string part) =>
        part is "" or "." or ".." || part.Contains(':') || part.EndsWith('.') || part.EndsWith(' ')
        || DeviceNames.Contains(part.Split('.')[0].TrimEnd(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A replace cut short between its two folder moves leaves only id.replaced: move it back, or drop it if the new copy
    /// made it. Packed copies (mod.mgsv) kept by builds before 0.9.40 are deleted; only the extracted files are used.
    /// </summary>
    public void Tidy()
    {
        if (!Directory.Exists(Dir))
            return;
        // copies from before 0.9.40 kept mod.mgsv next to the extracted files; a .mgsv taken out of a .zip is read from mod.mgsv now
        // best effort: a file held by antivirus or the indexer must not stop the game folder opening
        try
        {
            foreach (string mod in Directory.GetDirectories(Dir))
                if (Directory.Exists(Path.Combine(mod, "files")) && File.Exists(Path.Combine(mod, "mod.mgsv")))
                {
                    File.SetAttributes(Path.Combine(mod, "mod.mgsv"), FileAttributes.Normal);
                    File.Delete(Path.Combine(mod, "mod.mgsv"));
                }
            foreach (string unfinished in Directory.GetDirectories(Dir, "*.adding"))
                AtomicFile.DeleteDirectory(unfinished); // an add that was interrupted; the mod was never recorded
            foreach (string old in Directory.GetDirectories(Dir, "*.replaced"))
            {
                string final = old[..^".replaced".Length];
                if (Directory.Exists(final))
                    AtomicFile.DeleteDirectory(old);
                else
                    Directory.Move(old, final);
                Log.Warn($"[Library] Tidied up {Path.GetFileName(final)} after a replace that was interrupted last time");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"[Library] Couldn't tidy up everything, the rest is done next start ({e.Message})");
        }
    }

    public LibraryMod Load(string id)
    {
        string dir = Path.Combine(Dir, id);
        string manifestPath = Path.Combine(dir, "manifest.json");
        LibraryManifest manifest = JsonSerializer.Deserialize(File.ReadAllText(manifestPath), LibraryJsonContext.Default.LibraryManifest)
            ?? throw new InvalidDataException($"{manifestPath} is empty");
        manifest.Files = new Dictionary<string, LibraryFile>(manifest.Files, StringComparer.OrdinalIgnoreCase);
        ModEntry metadata;
        string metadataPath = Path.Combine(dir, "metadata.xml");
        if (!File.Exists(metadataPath))
            metadataPath = Path.Combine(dir, "files", "metadata.xml"); // an extracted copy
        using (FileStream stream = File.OpenRead(metadataPath))
            metadata = ModEntry.Parse(stream);
        if (string.IsNullOrWhiteSpace(metadata.Name))
            metadata.Name = manifest.Name; // the file name it was added under
        // DMM or its mods folder moved: the same file under the current mods folder is used; its size, date or MD5 is still checked before use.
        // While the old file is still there (an older DMM folder), the one in the current mods folder is used only if it is the same file
        // (same size and date), so a different build under the same name never takes over a copy that works
        if (manifest.PackagePath is { } package && Path.IsPathRooted(package) && !InModsFolder(package) && Relocated(package) is { } moved
            && (!File.Exists(package) || SameFile(package, moved)))
        {
            Log.Info($"[Library] {manifest.Name}: reading {moved} (added from {package})");
            manifest.PackagePath = moved;
        }
        return new LibraryMod { Id = id, Dir = dir, Metadata = metadata, Manifest = manifest };
    }

    private bool InModsFolder(string path) =>
        ModsFolder != null && Path.GetFullPath(path).StartsWith(Path.GetFullPath(ModsFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool SameFile(string a, string b)
    {
        var x = new FileInfo(a);
        var y = new FileInfo(b);
        return x.Length == y.Length && x.LastWriteTimeUtc == y.LastWriteTimeUtc;
    }

    // the old path's file name with its parent folders, then fewer of them, under the current mods folder
    private string? Relocated(string path)
    {
        if (ModsFolder is not { } folder)
            return null;
        string[] parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // the longest match first, so sub\b.mgsv is not mistaken for an unrelated b.mgsv at the top of the folder
        for (int take = Math.Min(4, parts.Length - 1); take >= 1; take--)
        {
            string candidate = Path.GetFullPath(Path.Combine([folder, .. parts[^take..]]));
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// Checks a package and records it in the library, replacing a mod with the same name; the files stay in the package.
    /// With takePackage the package file is moved into the library (a .mgsv taken out of a .zip). Throws when the package
    /// lists files it does not contain, which SnakeBite would fail on mid-install.
    /// </summary>
    public AddModResult Add(string mgsvPath, Version gameVersion, QarDictionary? dictionary = null, bool takePackage = false, string? samePackageMd5 = null)
    {
        var warnings = new List<string>();
        string id;
        string staging;
        using (MgsvPackage package = MgsvPackage.Open(mgsvPath))
        {
            ModEntry meta = package.Metadata;
            HashSet<string> used = Referenced(package, dictionary, gameVersion, warnings);
            id = MakeId(meta.Name);
            staging = Path.Combine(Dir, id + ".adding");
            if (Directory.Exists(staging))
                AtomicFile.DeleteDirectory(staging);
            Directory.CreateDirectory(staging);

            var manifest = new LibraryManifest { Name = meta.Name, Version = meta.Version, SourceFileName = Path.GetFileName(mgsvPath) };
            byte[] buffer = new byte[1 << 20];
            // only what metadata.xml installs is listed: readmes, previews and junk entries (stream names, odd paths) are never read
            foreach (string name in package.FileNames.Where(used.Contains))
            {
                using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
                long size = 0;
                using (Stream input = package.OpenFile(name))
                {
                    int read;
                    while ((read = input.Read(buffer)) > 0)
                    {
                        md5.AppendData(buffer, 0, read);
                        size += read;
                    }
                }
                manifest.Files[name] = new LibraryFile { Size = size, Md5 = Convert.ToHexString(md5.GetHashAndReset()) };
            }
            using (Stream input = package.OpenFile("metadata.xml"))
            using (var output = new FileStream(Path.Combine(staging, "metadata.xml"), FileMode.CreateNew, FileAccess.Write))
                input.CopyTo(output);
            manifest.PackageMd5 = Md5Key.ToHex(Md5Key.OfFile(mgsvPath));
            if (samePackageMd5 != null && manifest.PackageMd5 != samePackageMd5)
            {
                AtomicFile.DeleteDirectory(staging);
                throw new InvalidDataException($"{Path.GetFileName(mgsvPath)} is a different build of {meta.Name} {meta.Version} than the one in the load order");
            }
            manifest.SourceModifiedUtc = File.GetLastWriteTimeUtc(mgsvPath);
            manifest.PackagePath = takePackage ? "mod.mgsv" : Path.GetFullPath(mgsvPath);
            var info = new FileInfo(mgsvPath);
            (manifest.PackageSize, manifest.PackageModifiedUtc) = (info.Length, info.LastWriteTimeUtc);
            AtomicFile.WriteAllText(Path.Combine(staging, "manifest.json"), JsonSerializer.Serialize(manifest, LibraryJsonContext.Default.LibraryManifest));
        }
        if (takePackage)
            File.Move(mgsvPath, Path.Combine(staging, "mod.mgsv"));

        string final = Path.Combine(Dir, id);
        bool replaced = Directory.Exists(final);
        if (replaced)
        {
            string old = Path.Combine(Dir, id + ".replaced");
            if (Directory.Exists(old))
                AtomicFile.DeleteDirectory(old);
            Directory.Move(final, old);
            Directory.Move(staging, final);
            try { AtomicFile.DeleteDirectory(old); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Warn($"[Library] {id}: couldn't delete the old copy yet, it's deleted next start ({e.Message})"); }
        }
        else
        {
            Directory.Move(staging, final);
        }
        return new AddModResult(Load(id), replaced, warnings);
    }

    /// <summary>Adds a mod found by ModSources; a .mgsv inside a .zip is extracted to tempDir first.</summary>
    public AddModResult Add(ModSource source, Version gameVersion, string tempDir, QarDictionary? dictionary = null, string? samePackageMd5 = null)
    {
        // a .mgsv inside a .zip goes into the mods folder as a file of its own, read from there like any other; DMM's own
        // folder keeps a copy only when the mods folder cannot take it
        if (source.InnerEntry != null && IntoModsFolder(source, tempDir) is ({ } extracted, bool created))
        {
            try
            {
                return Add(extracted, gameVersion, dictionary, takePackage: false, samePackageMd5) with { TakenOutOfZip = created };
            }
            catch when (created)
            {
                // a mod that fails its checks (or a different build than asked for) leaves nothing new in the mods folder
                try { File.Delete(extracted); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                throw;
            }
        }
        string path = ModSources.Materialize(source, tempDir);
        try
        {
            AddModResult result = Add(path, gameVersion, dictionary, takePackage: source.InnerEntry != null, samePackageMd5);
            if (source.InnerEntry != null)
            {
                result.Mod.Manifest.SourceFileName = source.DisplayFile;
                AtomicFile.WriteAllText(Path.Combine(result.Mod.Dir, "manifest.json"), JsonSerializer.Serialize(result.Mod.Manifest, LibraryJsonContext.Default.LibraryManifest));
            }
            return result;
        }
        finally
        {
            if (source.InnerEntry != null && File.Exists(path))
                File.Delete(path);
        }
    }

    // the same file already there (added before) is used as it is; another file under that name gets a numbered name.
    // Created is false when that file was already there.
    private (string Path, bool Created)? IntoModsFolder(ModSource source, string tempDir)
    {
        if (ModsFolder is not { } folder)
            return null;
        string? part = null;
        string temp = ModSources.Materialize(source, tempDir);
        try
        {
            Directory.CreateDirectory(folder);
            string name = Path.GetFileName(source.InnerEntry!);
            string target = Path.Combine(folder, name);
            for (int n = 2; File.Exists(target); n++)
            {
                if (new FileInfo(target).Length == new FileInfo(temp).Length && Md5Key.OfFile(target) == Md5Key.OfFile(temp))
                    return (Path.GetFullPath(target), false);
                target = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)} ({n}){Path.GetExtension(name)}");
            }
            // moved in under a name the scan skips, then renamed, so the folder watcher never reads half a file
            part = target + ".part";
            File.Move(temp, part, overwrite: true);
            File.Move(part, target);
            return (Path.GetFullPath(target), true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { if (part != null && File.Exists(part)) File.Delete(part); } catch (Exception again) when (again is IOException or UnauthorizedAccessException) { }
            Log.Warn($"[Library] Couldn't put {Path.GetFileName(source.InnerEntry!)} in the Mods folder, so DMM keeps its own copy ({e.Message})");
            return null;
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    public void Remove(string id)
    {
        string dir = Path.Combine(Dir, id);
        if (Directory.Exists(dir))
            AtomicFile.DeleteDirectory(dir);
    }

    /// <summary>Returns the files that are missing or changed: the package, or for an extracted copy the files whose size differs.</summary>
    public static List<string> CheckFiles(LibraryMod mod)
    {
        if (mod.PackageFile is { } package)
        {
            var info = new FileInfo(package);
            if (!info.Exists || info.Length != mod.Manifest.PackageSize)
                return [package];
            if (info.LastWriteTimeUtc == mod.Manifest.PackageModifiedUtc)
                return [];
            if (Md5Key.ToHex(Md5Key.OfFile(package)) != mod.Manifest.PackageMd5)
                return [package];
            // same content with a new date (touched, synced, restored): remember the date so it is not hashed on every apply
            mod.Manifest.PackageModifiedUtc = info.LastWriteTimeUtc;
            try { AtomicFile.WriteAllText(Path.Combine(mod.Dir, "manifest.json"), JsonSerializer.Serialize(mod.Manifest, LibraryJsonContext.Default.LibraryManifest)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Warn($"[Library] {mod.Id}: couldn't update its saved details ({e.Message})"); }
            return [];
        }
        var problems = new List<string>();
        foreach ((string name, LibraryFile file) in mod.Manifest.Files)
        {
            var info = new FileInfo(Path.Combine(mod.FilesDir, DiskName(name)));
            if (!info.Exists || info.Length != file.Size)
                problems.Add(name);
        }
        return problems;
    }

    /// <summary>
    /// The zip names metadata.xml installs, in package spelling. Throws when it lists a file the package lacks, which SnakeBite
    /// would fail on mid-install; entries SnakeBite skips (not a .dat type, game files, paths leaving the game folder) are not
    /// required. A QAR file may be stored under its qar_dictionary name, as SnakeBite accepts.
    /// </summary>
    private static HashSet<string> Referenced(MgsvPackage package, QarDictionary? dictionary, Version gameVersion, List<string> warnings)
    {
        ModEntry meta = package.Metadata;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "metadata.xml" };
        var missing = new List<string>();
        foreach (ModQarEntry qar in meta.ModQarEntries)
        {
            if (string.IsNullOrEmpty(qar.FilePath))
                continue;
            if (!FpkLayout.IsExtensionValidForArchive(qar.FilePath, ".dat") && !qar.FilePath.Contains("wmv", StringComparison.Ordinal))
            {
                warnings.Add($"{qar.FilePath} isn't a file type the .dat files take, so it's skipped (SnakeBite skips it too)");
                continue;
            }
            string original = MgsvPackage.QarFileName(qar.FilePath);
            string? renamed = !qar.FilePath.StartsWith("/Assets/", StringComparison.Ordinal) && dictionary?.UpdateName(qar.FilePath) is { } name
                ? MgsvPackage.QarFileName(name) : null;
            bool found = false;
            foreach (string? candidate in new[] { original, renamed })
            {
                if (candidate != null && package.Contains(candidate))
                {
                    used.Add(MgsvPackage.NormalizeName(candidate));
                    found = true;
                }
            }
            if (!found)
                missing.Add(qar.FilePath);
        }
        foreach (ModFileEntry file in meta.ModFileEntries)
        {
            string relative = file.FilePath.Replace('\\', '/').TrimStart('/');
            if (Engine.ModFiles.IsIgnored(file.FilePath) || !Engine.ModFiles.IsSafeRelative(relative))
                continue; // skipped at install, with a warning there
            string zipName = MgsvPackage.GameDirFileName(file.FilePath);
            if (package.Contains(zipName))
                used.Add(MgsvPackage.NormalizeName(zipName));
            else
                missing.Add(zipName);
        }
        if (missing.Count > 0)
            throw new InvalidDataException($"{meta.Name}: the mod is missing files it says it has: {string.Join(", ", missing.Take(10))}{(missing.Count > 10 ? $" and {missing.Count - 10} more" : "")}");

        foreach (ModFpkEntry fpk in meta.ModFpkEntries)
            if (!FpkLayout.IsExtensionValidForArchive(fpk.FilePath, fpk.FpkFile))
                warnings.Add($"{fpk.FilePath} isn't a file type the game reads in {Path.GetFileName(fpk.FpkFile)}, so it's ignored. That's harmless, probably a leftover from making the mod");

        if (!Game.GameLayout.ModVersionOk(gameVersion, meta.MGSVersion.AsVersion()))
            warnings.Add($"made for game version {meta.MGSVersion.Value}, yours is {gameVersion}");
        return used;
    }
}
