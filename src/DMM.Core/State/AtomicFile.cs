// Crash-safe small file writes: temp file, flush to disk, replace
namespace DMM.Core.State;

public static class AtomicFile
{
    public static void WriteAllBytes(string path, ReadOnlySpan<byte> bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        // a read-only target, or one held open for a moment (antivirus, indexer, sync tools), refuses the replace with access denied
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                if (File.Exists(path))
                    File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < 10)
            {
                Thread.Sleep(300);
            }
        }
    }

    /// <summary>File.Copy, then flushed to disk, for copies a journal moves into place later.</summary>
    public static void CopyDurable(string source, string target)
    {
        File.Copy(source, target, overwrite: true);
        File.SetAttributes(target, File.GetAttributes(target) & ~FileAttributes.ReadOnly); // File.Copy keeps the source's read-only flag
        using var stream = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.Flush(flushToDisk: true);
    }

    /// <summary>Directory.Delete(recursive) stops at a read-only file or folder on Windows, so the flag is cleared first.</summary>
    public static void DeleteDirectory(string path)
    {
        var root = new DirectoryInfo(path);
        if (!root.Attributes.HasFlag(FileAttributes.ReparsePoint))
            ClearReadOnly(root);
        Directory.Delete(path, true);
    }

    // junctions and links are left alone: Directory.Delete removes the link, never what it points to
    private static void ClearReadOnly(DirectoryInfo dir)
    {
        if (dir.Attributes.HasFlag(FileAttributes.ReadOnly))
            dir.Attributes &= ~FileAttributes.ReadOnly;
        foreach (FileSystemInfo entry in dir.EnumerateFileSystemInfos())
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                continue;
            if (entry is DirectoryInfo sub)
                ClearReadOnly(sub);
            else if (entry.Attributes.HasFlag(FileAttributes.ReadOnly))
                entry.Attributes &= ~FileAttributes.ReadOnly;
        }
    }

    public static void WriteAllText(string path, string text) => WriteAllBytes(path, new System.Text.UTF8Encoding(false).GetBytes(text));
}
