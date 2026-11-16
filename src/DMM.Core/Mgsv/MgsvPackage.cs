// Read-only view of a .mgsv mod package (zip with metadata.xml)
using System.IO.Compression;

namespace DMM.Core.Mgsv;

/// <summary>
/// A SnakeBite package: metadata.xml at the zip root, QAR files at their paths, game folder files under GameDir/.
/// A zip holding the same layout inside one top folder ("ModName/metadata.xml") is read the same way.
/// </summary>
public sealed class MgsvPackage : IDisposable
{
    /// <summary>Zip names without the UTF-8 flag are in the OEM code page, as SharpZipLib (MakeBite, SnakeBite) writes and reads them.</summary>
    public static readonly System.Text.Encoding EntryNames = EntryNamesFor(System.Globalization.CultureInfo.CurrentCulture);

    public static System.Text.Encoding EntryNamesFor(System.Globalization.CultureInfo culture)
    {
        _ = Hashing.AnsiText.Encoding; // registers the code page provider
        // Unicode-only locales (hi-IN, ka-GE, ...) report 1 (CP_OEMCP), which is not a code page; SharpZipLib falls back to 437 too
        try
        {
            return System.Text.Encoding.GetEncoding(culture.TextInfo.OEMCodePage);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException)
        {
            return System.Text.Encoding.GetEncoding(437);
        }
    }

    public const string GameDirFolder = "GameDir";

    private readonly Stream stream;
    private readonly ZipArchive zip;
    private readonly Dictionary<string, ZipArchiveEntry> files;

    public string Path { get; }
    public ModEntry Metadata { get; }

    private MgsvPackage(string path, Stream stream, ZipArchive zip, ModEntry metadata, Dictionary<string, ZipArchiveEntry> files)
    {
        Path = path;
        this.stream = stream;
        this.zip = zip;
        Metadata = metadata;
        this.files = files;
    }

    public static MgsvPackage Open(string path) => Open(File.OpenRead(path), path);

    /// <summary>Takes ownership of the stream, which must be seekable.</summary>
    public static MgsvPackage Open(Stream stream, string displayPath)
    {
        ZipArchive zip;
        try
        {
            zip = OpenZip(stream);
        }
        catch (Exception e)
        {
            stream.Dispose();
            if (e is not InvalidDataException)
                throw;
            throw new InvalidDataException($"{System.IO.Path.GetFileName(displayPath)} is not a zip archive, or is damaged ({e.Message})");
        }
        try
        {
            CheckSizes(zip, stream.Length, displayPath);
            string root = FindRoot(zip) ?? throw new InvalidDataException($"{System.IO.Path.GetFileName(displayPath)} isn't a SnakeBite mod (it has no metadata.xml)");
            var files = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                string name = NormalizeName(entry.FullName);
                if (name.Length == 0 || name.EndsWith('/') || !name.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    continue;
                files[name[root.Length..]] = entry;
            }

            ModEntry metadata;
            if (files["metadata.xml"].Length > 16 << 20)
                throw new InvalidDataException($"{System.IO.Path.GetFileName(displayPath)}: metadata.xml is over 16 MB");
            using (Stream metadataStream = files["metadata.xml"].Open())
                metadata = ModEntry.Parse(metadataStream);
            if (string.IsNullOrWhiteSpace(metadata.Name))
                metadata.Name = System.IO.Path.GetFileNameWithoutExtension(displayPath); // SnakeBite installs these too; the file name stands in
            return new MgsvPackage(displayPath, stream, zip, metadata, files);
        }
        catch
        {
            zip.Dispose();
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens a zip without taking the stream. A zip with other data in front of it (a .mgsv stored uncompressed in a .rar
    /// that was renamed to .mgsv) is read from where it starts, as SharpZipLib (SnakeBite) and 7-Zip do.
    /// </summary>
    private static ZipArchive OpenZip(Stream stream)
    {
        ZipArchive? zip = null;
        long start = 0;
        try
        {
            zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true, EntryNames);
            _ = zip.Entries; // the central directory is read here, not in the constructor
            return zip;
        }
        catch (InvalidDataException) when ((start = EmbeddedZipStart(stream)) > 0)
        {
            zip?.Dispose();
        }
        catch
        {
            zip?.Dispose();
            throw;
        }
        zip = new ZipArchive(new OffsetStream(stream, start), ZipArchiveMode.Read, leaveOpen: false, EntryNames);
        try
        {
            _ = zip.Entries;
            return zip;
        }
        catch
        {
            zip.Dispose();
            throw;
        }
    }

    /// <summary>Bytes in front of the zip, from where its end record says the central directory is; 0 when none or unknown.</summary>
    private static long EmbeddedZipStart(Stream stream)
    {
        long tail = Math.Min(stream.Length, 0xFFFF + 22);
        var buffer = new byte[tail];
        stream.Position = stream.Length - tail;
        stream.ReadExactly(buffer);
        for (int i = buffer.Length - 22; i >= 0; i--)
        {
            if (BitConverter.ToUInt32(buffer, i) != 0x06054B50)
                continue;
            long size = BitConverter.ToUInt32(buffer, i + 12), offset = BitConverter.ToUInt32(buffer, i + 16);
            long directory = stream.Length - tail + i - size;
            if (offset == 0xFFFFFFFF || directory <= offset)
                return 0; // Zip64, or nothing in front
            var signature = new byte[4];
            stream.Position = directory;
            stream.ReadExactly(signature);
            return BitConverter.ToUInt32(signature) == 0x02014B50 ? directory - offset : 0;
        }
        return 0;
    }

    /// <summary>Read-only view of a stream from an offset on; leaves the stream open.</summary>
    private sealed class OffsetStream(Stream inner, long start) : Stream
    {
        private long position;
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => inner.Length - start;
        public override long Position { get => position; set => position = value; }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            inner.Position = start + position;
            int read = inner.Read(buffer);
            position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => position + offset,
            _ => Length + offset,
        };

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// Deflate packs at most about 1030:1, so entries declaring more than that in total share their data (a zip bomb)
    /// and would unpack to far more than the file holds.
    /// </summary>
    public static void CheckSizes(ZipArchive zip, long fileLength, string displayPath)
    {
        long limit = 1100 * Math.Max(fileLength, 1024), total = 0;
        foreach (ZipArchiveEntry entry in zip.Entries)
            if (entry.Length > limit - total || (total += entry.Length) > limit)
                throw new InvalidDataException($"{System.IO.Path.GetFileName(displayPath)} looks damaged (it claims to hold far more data than it does), so DMM won't read it");
    }

    /// <summary>"" when metadata.xml is at the root, "Folder/" when the only metadata.xml is one folder down, else null.</summary>
    private static string? FindRoot(ZipArchive zip)
    {
        var metadata = zip.Entries.Select(e => NormalizeName(e.FullName))
            .Where(n => n.Equals("metadata.xml", StringComparison.OrdinalIgnoreCase) || n.EndsWith("/metadata.xml", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (metadata.Any(n => n.Equals("metadata.xml", StringComparison.OrdinalIgnoreCase)))
            return "";
        var oneDown = metadata.Where(n => n.Count(c => c == '/') == 1).ToList();
        return oneDown.Count == 1 ? oneDown[0][..(oneDown[0].IndexOf('/') + 1)] : null;
    }

    /// <summary>Zip path form used for lookups: forward slashes, no leading slash.</summary>
    public static string NormalizeName(string name) => name.Replace('\\', '/').TrimStart('/');

    /// <summary>Zip name of a QAR entry ("/Assets/a.fpk" is stored at "Assets/a.fpk").</summary>
    public static string QarFileName(string qarPath) => NormalizeName(qarPath);

    /// <summary>Zip name of a game folder file ("/plugins/x.dll" is stored at "GameDir/plugins/x.dll").</summary>
    public static string GameDirFileName(string filePath) => GameDirFolder + "/" + NormalizeName(filePath);

    public IEnumerable<string> FileNames => files.Keys;

    public bool Contains(string zipName) => files.ContainsKey(NormalizeName(zipName));

    public long Size(string zipName) => files[NormalizeName(zipName)].Length;

    public Stream OpenFile(string zipName) => files[NormalizeName(zipName)].Open();

    public void Dispose()
    {
        zip.Dispose();
        stream.Dispose();
    }
}
