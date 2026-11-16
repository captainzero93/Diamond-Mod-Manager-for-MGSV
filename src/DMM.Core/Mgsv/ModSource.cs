// Mod files on disk: .mgsv packages and .zip downloads
using System.IO.Compression;

namespace DMM.Core.Mgsv;

/// <summary>
/// One installable mod found on disk. InnerEntry is set when the mod is a .mgsv inside a .zip.
/// </summary>
public sealed record ModSource(string FilePath, string? InnerEntry, ModEntry Metadata, long Size, DateTime ModifiedUtc)
{
    public string DisplayFile => InnerEntry == null ? Path.GetFileName(FilePath) : $"{Path.GetFileName(FilePath)} > {Path.GetFileName(InnerEntry)}";
}

/// <summary>InUse: another program had the file open (a download or copy still writing, antivirus); worth trying again.</summary>
public sealed record ModSourceError(string FilePath, string Message, bool InUse = false);

public static class ModSources
{
    public static readonly string[] Extensions = [".mgsv", ".zip"];

    private static readonly Dictionary<string, (long Size, DateTime Modified, List<ModSource>? Sources, string? Error)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    public static bool IsModFile(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Mods in a file: a .mgsv, or every .mgsv inside a .zip. A .zip of loose mod files is refused: only a package built by
    /// MakeBite or DMM carries a metadata.xml that matches its files. Throws InvalidDataException when the file holds no mod.
    /// </summary>
    public static List<ModSource> Read(string path)
    {
        var info = new FileInfo(path);
        if (!Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using MgsvPackage package = MgsvPackage.Open(path);
            return [new ModSource(info.FullName, null, package.Metadata, info.Length, info.LastWriteTimeUtc)];
        }
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Read, MgsvPackage.EntryNames);
        MgsvPackage.CheckSizes(zip, info.Length, path);

        var sources = new List<ModSource>();
        var errors = new List<string>();
        foreach (ZipArchiveEntry entry in zip.Entries.Where(e => e.FullName.EndsWith(".mgsv", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                using MgsvPackage package = MgsvPackage.Open(Copy(entry), path + "!/" + entry.FullName); // "!/" so a nameless package is named after its own file
                sources.Add(new ModSource(info.FullName, entry.FullName, package.Metadata, info.Length, info.LastWriteTimeUtc));
            }
            catch (Exception e) when (e is InvalidDataException or InvalidOperationException or System.Xml.XmlException)
            {
                errors.Add(e.InnerException?.Message ?? e.Message); // one bad package does not hide the others in the zip
            }
        }
        if (sources.Count > 0)
            return sources;
        if (errors.Count > 0)
            throw new InvalidDataException(string.Join("; ", errors));
        // a .mgsv renamed to .zip (or "x.mgsv.zip") is the package itself, with metadata.xml at its root; a zipped folder of
        // loose files (metadata.xml one folder down, or none) stays refused
        bool isPackage = zip.Entries.Any(e => MgsvPackage.NormalizeName(e.FullName).Equals("metadata.xml", StringComparison.OrdinalIgnoreCase));
        zip.Dispose();
        if (!isPackage)
            throw new InvalidDataException($"{Path.GetFileName(path)} holds no .mgsv. A .zip must contain a .mgsv built with MakeBite or DMM; loose mod files are not installed.");
        try
        {
            using MgsvPackage package = MgsvPackage.Open(path);
            return [new ModSource(info.FullName, null, package.Metadata, info.Length, info.LastWriteTimeUtc)];
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)} holds no .mgsv. A .zip must contain a .mgsv built with MakeBite or DMM; loose mod files are not installed.");
        }
    }

    /// <summary>Mods in a folder and its subfolders. Results are cached by file size and time.</summary>
    public static (List<ModSource> Mods, List<ModSourceError> Errors) Scan(string folder)
    {
        var mods = new List<ModSource>();
        var errors = new List<ModSourceError>();
        if (!Directory.Exists(folder))
            return (mods, errors);
        // folders that cannot be read (junctions in Documents, permissions) are skipped instead of failing the whole scan
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
        foreach (string file in Directory.EnumerateFiles(folder, "*", options).Where(IsModFile).Order(StringComparer.OrdinalIgnoreCase))
        {
            var info = new FileInfo(file);
            long size;
            DateTime modified;
            try
            {
                size = info.Length;
                modified = info.LastWriteTimeUtc;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue; // deleted or moved while the folder was read
            }
            (long Size, DateTime Modified, List<ModSource>? Sources, string? Error) cached;
            lock (Gate)
                Cache.TryGetValue(file, out cached);
            if (cached.Size != size || cached.Modified != modified)
            {
                try
                {
                    cached = (size, modified, Read(file), null);
                }
                // sharing or lock violation (Windows 32/33, EWOULDBLOCK elsewhere): not cached, tried again
                catch (IOException e) when ((e.HResult & 0xFFFF) is 32 or 33 || (!OperatingSystem.IsWindows() && e.HResult == 11))
                {
                    errors.Add(new ModSourceError(file, e.Message, InUse: true));
                    continue;
                }
                catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidOperationException
                                              or NotSupportedException or OutOfMemoryException)
                {
                    cached = (size, modified, null, e.InnerException?.Message ?? e.Message);
                }
                lock (Gate)
                    Cache[file] = cached;
            }
            if (cached.Sources != null)
                mods.AddRange(cached.Sources);
            else
                errors.Add(new ModSourceError(file, cached.Error ?? "unreadable"));
        }
        return (mods, errors);
    }

    /// <summary>Path of a .mgsv for the source, extracting it from its .zip into tempDir when needed.</summary>
    public static string Materialize(ModSource source, string tempDir)
    {
        if (source.InnerEntry == null)
            return source.FilePath;
        Directory.CreateDirectory(tempDir);
        string target = Path.Combine(tempDir, Path.GetFileName(source.InnerEntry));
        using ZipArchive zip = ZipFile.Open(source.FilePath, ZipArchiveMode.Read, MgsvPackage.EntryNames);
        ZipArchiveEntry entry = zip.GetEntry(source.InnerEntry) ?? throw new FileNotFoundException($"{source.InnerEntry} is no longer in {source.FilePath}");
        entry.ExtractToFile(target, overwrite: true);
        return target;
    }

    // small packages are read in memory; large ones go through a temporary file that deletes itself when closed
    private static Stream Copy(ZipArchiveEntry entry)
    {
        Stream copy = entry.Length <= 256L << 20
            ? new MemoryStream((int)entry.Length)
            : new FileStream(Path.Combine(Path.GetTempPath(), "DMM-" + Guid.NewGuid().ToString("N") + ".mgsv"), FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.None, 1 << 16, FileOptions.DeleteOnClose);
        try
        {
            using (Stream stream = entry.Open())
                stream.CopyTo(copy);
            copy.Position = 0;
            return copy;
        }
        catch
        {
            copy.Dispose();
            throw;
        }
    }
}
