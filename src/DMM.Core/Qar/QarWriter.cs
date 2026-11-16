// QAR writer: raw reuse, parallel encode, read-back check
using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using DMM.Core.Hashing;

namespace DMM.Core.Qar;

public abstract record QarSource;

/// <summary>Copy an existing entry unchanged (header and stored data).</summary>
public sealed record RawQarSource(QarArchive Archive, QarEntryInfo Entry) : QarSource;

/// <summary>Encode file content held in memory.</summary>
public sealed record BytesQarSource(byte[] Content, bool Compress) : QarSource;

/// <summary>Encode a file read from Open (a file on disk or an entry of a mod package). Md5 of the content is computed when not given.</summary>
public sealed record FileQarSource(Func<Stream> Open, long Length, bool Compress, UInt128? Md5 = null) : QarSource
{
    public static FileQarSource OnDisk(string path, bool compress) => new(() => File.OpenRead(path), new FileInfo(path).Length, compress);
}

/// <summary>Decode an entry of another archive and encode it again (GzsTool extract then repack).</summary>
public sealed record DecodedQarSource(QarArchive Archive, QarEntryInfo Entry, bool Compress) : QarSource;

public sealed record QarWriteItem(ulong Hash, string Name, QarSource Source);

public sealed record QarWriteResult(int Reused, int Encoded, long Length);

/// <summary>
/// Output layout matches GzsTool.Core QarFile.Write: 32 byte header, section table, entries aligned to the block size,
/// unknown table empty. Entries are encoded like GzsTool QarEntry.Write (MD5 of the content before compression,
/// zlib for compressed entries, Decrypt1 cipher keyed by the low hash dword). Lua entries are written unencrypted, as GzsTool does.
/// </summary>
public static class QarWriter
{
    private const int CopyBufferSize = 1 << 20;
    private const long BatchBytes = 192L << 20;
    private const int BatchItems = 512;

    public static QarWriteResult Write(string outputPath, uint flags, IReadOnlyList<QarWriteItem> items, bool verify = true)
    {
        var seen = new HashSet<ulong>();
        foreach (QarWriteItem item in items)
            if (!seen.Add(item.Hash))
                throw new InvalidOperationException($"Duplicate QAR hash {item.Hash:x16} ({item.Name}) in write list");

        int shift = QarCipher.BlockShift(flags);
        int alignment = 1 << shift;
        var sections = new ulong[items.Count];
        int reused = 0, encoded = 0;
        var encodedChecks = new List<(int Index, UInt128 Md5)>();

        using (var output = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 20))
        {
            long dataStart = Align(QarCipher.HeaderSize + 8L * items.Count, alignment);
            output.SetLength(dataStart);
            output.Position = dataStart;
            byte[] buffer = new byte[CopyBufferSize];

            int batchStart = 0;
            while (batchStart < items.Count)
            {
                int batchEnd = NextBatchEnd(items, batchStart);
                var prepared = new PreparedEntry?[batchEnd - batchStart];
                try
                {
                    Parallel.For(batchStart, batchEnd, i => prepared[i - batchStart] = Prepare(items[i]));
                }
                catch (AggregateException e)
                {
                    // the first real error, so callers see an IOException and not "One or more errors occurred"
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.Flatten().InnerExceptions[0]).Throw();
                }

                for (int i = batchStart; i < batchEnd; i++)
                {
                    QarWriteItem item = items[i];
                    if (output.Position >> shift > 0xFFFFFF)
                        throw new InvalidOperationException($"{outputPath}: archive is too large for the QAR section table");
                    sections[i] = ((ulong)(output.Position >> shift) << 40) | ((item.Hash & 0xFF) << 32) | ((item.Hash >> 32) & 0xFFFFFFFFFF);

                    PreparedEntry? entry = prepared[i - batchStart];
                    switch (item.Source)
                    {
                        case RawQarSource raw:
                            raw.Archive.CopyRaw(raw.Entry, output, buffer);
                            reused++;
                            break;
                        case FileQarSource file when entry!.Raw == null:
                            WriteStreamedFile(output, item, file, entry.Md5, buffer);
                            encoded++;
                            break;
                        default:
                            output.Write(entry!.Raw!);
                            if (entry.Compressed || item.Source is DecodedQarSource)
                                encodedChecks.Add((i, entry.Md5));
                            encoded++;
                            break;
                    }
                    prepared[i - batchStart] = null;
                    PadTo(output, alignment);
                }
                batchStart = batchEnd;
            }

            long dataEnd = output.Position;
            if (dataEnd >> shift > uint.MaxValue)
                throw new InvalidOperationException($"{outputPath}: archive end does not fit the header");

            Span<byte> header = stackalloc byte[QarCipher.HeaderSize];
            BinaryPrimitives.WriteUInt32LittleEndian(header, QarCipher.Magic);
            BinaryPrimitives.WriteUInt32LittleEndian(header[4..], flags ^ QarCipher.Mask1);
            BinaryPrimitives.WriteUInt32LittleEndian(header[8..], (uint)items.Count ^ QarCipher.Mask2);
            BinaryPrimitives.WriteUInt32LittleEndian(header[12..], QarCipher.Mask3); // unknown table count 0
            BinaryPrimitives.WriteUInt32LittleEndian(header[16..], (uint)(dataEnd >> shift) ^ QarCipher.Mask4);
            BinaryPrimitives.WriteUInt32LittleEndian(header[20..], (uint)dataStart ^ QarCipher.Mask1);
            BinaryPrimitives.WriteUInt32LittleEndian(header[24..], 1 ^ QarCipher.Mask1);
            BinaryPrimitives.WriteUInt32LittleEndian(header[28..], 0 ^ QarCipher.Mask2);

            byte[] sectionBytes = new byte[8 * items.Count];
            for (int i = 0; i < sections.Length; i++)
                BinaryPrimitives.WriteUInt64LittleEndian(sectionBytes.AsSpan(i * 8), sections[i]);
            QarCipher.CryptSections(sectionBytes);

            output.Position = 0;
            output.Write(header);
            output.Write(sectionBytes);
            output.Flush(flushToDisk: true);
        }

        long length = new FileInfo(outputPath).Length;
        if (verify)
            Verify(outputPath, flags, items, encodedChecks);
        return new QarWriteResult(reused, encoded, length);
    }

    private sealed class PreparedEntry
    {
        public byte[]? Raw;
        public UInt128 Md5;
        public bool Compressed;
    }

    private static int NextBatchEnd(IReadOnlyList<QarWriteItem> items, int start)
    {
        long bytes = 0;
        int end = start;
        while (end < items.Count && end - start < BatchItems && bytes < BatchBytes)
        {
            bytes += items[end].Source switch
            {
                BytesQarSource b => b.Content.Length,
                FileQarSource { Compress: true } f => f.Length,
                DecodedQarSource d => Math.Max(d.Entry.StoredSize, d.Entry.UncompressedSize),
                _ => 0,
            };
            end++;
        }
        return end;
    }

    private static PreparedEntry? Prepare(QarWriteItem item)
    {
        switch (item.Source)
        {
            case RawQarSource:
                return null;
            case BytesQarSource bytes:
                return Encode(item.Hash, bytes.Content, bytes.Compress);
            case DecodedQarSource decoded:
                return Encode(item.Hash, decoded.Archive.ReadContent(decoded.Entry), decoded.Compress);
            case FileQarSource { Compress: true } file:
            {
                byte[] content = new byte[file.Length];
                using (Stream input = file.Open())
                {
                    input.ReadExactly(content);
                    if (input.ReadByte() != -1)
                        throw new IOException($"{item.Name} changed since its size was recorded");
                }
                PreparedEntry encoded = Encode(item.Hash, content, true);
                if (file.Md5 is { } expected && encoded.Md5 != expected)
                    throw new IOException($"{item.Name} changed since its MD5 was recorded");
                return encoded;
            }
            case FileQarSource file:
                if (file.Md5 is { } known)
                    return new PreparedEntry { Md5 = known };
                using (Stream input = file.Open())
                    return new PreparedEntry { Md5 = Md5Key.From(MD5.HashData(input)) };
            default:
                throw new InvalidOperationException($"Unknown QAR source {item.Source.GetType().Name}");
        }
    }

    private static PreparedEntry Encode(ulong hash, byte[] content, bool compress)
    {
        UInt128 md5 = Md5Key.Of(content);
        byte[] stored = compress ? Deflate(content) : content;
        byte[] raw = new byte[QarCipher.EntryHeaderSize + stored.Length];
        WriteEntryHeader(raw, hash, (uint)stored.Length, (uint)content.Length, md5);
        stored.CopyTo(raw, QarCipher.EntryHeaderSize);
        QarCipher.CryptData(raw.AsSpan(QarCipher.EntryHeaderSize), (uint)(hash & 0xFFFFFFFF));
        return new PreparedEntry { Raw = raw, Md5 = md5, Compressed = compress };
    }

    private static byte[] Deflate(byte[] content)
    {
        using var output = new MemoryStream(content.Length / 2 + 64);
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(content);
        return output.ToArray();
    }

    private static void WriteEntryHeader(Span<byte> h, ulong hash, uint storedSize, uint uncompressedSize, UInt128 md5)
    {
        Span<byte> md5Bytes = stackalloc byte[16];
        Md5Key.Write(md5, md5Bytes);
        BinaryPrimitives.WriteUInt64LittleEndian(h, hash ^ QarCipher.HashMask);
        BinaryPrimitives.WriteUInt32LittleEndian(h[8..], storedSize ^ QarCipher.Mask2);
        BinaryPrimitives.WriteUInt32LittleEndian(h[12..], uncompressedSize ^ QarCipher.Mask3);
        BinaryPrimitives.WriteUInt32LittleEndian(h[16..], BinaryPrimitives.ReadUInt32LittleEndian(md5Bytes) ^ QarCipher.Mask4);
        BinaryPrimitives.WriteUInt32LittleEndian(h[20..], BinaryPrimitives.ReadUInt32LittleEndian(md5Bytes[4..]) ^ QarCipher.Mask1);
        BinaryPrimitives.WriteUInt32LittleEndian(h[24..], BinaryPrimitives.ReadUInt32LittleEndian(md5Bytes[8..]) ^ QarCipher.Mask1);
        BinaryPrimitives.WriteUInt32LittleEndian(h[28..], BinaryPrimitives.ReadUInt32LittleEndian(md5Bytes[12..]) ^ QarCipher.Mask2);
    }

    private static void WriteStreamedFile(FileStream output, QarWriteItem item, FileQarSource file, UInt128 md5, byte[] buffer)
    {
        ulong hash = item.Hash;
        string path = item.Name;
        using Stream input = file.Open();
        long size = file.Length;
        if (size > uint.MaxValue)
            throw new InvalidOperationException($"{path}: too large for a QAR entry");

        Span<byte> header = stackalloc byte[QarCipher.EntryHeaderSize];
        WriteEntryHeader(header, hash, (uint)size, (uint)size, md5);
        output.Write(header);

        using var check = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        uint hashLow = (uint)(hash & 0xFFFFFFFF);
        long offset = 0;
        int read;
        while ((read = input.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false)) > 0)
        {
            Span<byte> chunk = buffer.AsSpan(0, read);
            check.AppendData(chunk);
            QarCipher.CryptData(chunk, hashLow, offset);
            output.Write(chunk);
            offset += read;
        }
        if (offset != size || Md5Key.From(check.GetHashAndReset()) != md5)
            throw new IOException($"{path} changed while it was being written to the archive");
    }

    private static void Verify(string path, uint flags, IReadOnlyList<QarWriteItem> items, List<(int Index, UInt128 Md5)> encodedChecks)
    {
        using QarArchive archive = QarArchive.Open(path);
        if (archive.Flags != flags || archive.Entries.Count != items.Count)
            throw new InvalidDataException($"{path}: read-back found {archive.Entries.Count} entries with flags {archive.Flags}, expected {items.Count} with flags {flags}");
        if (archive.Length % (1 << archive.BlockShift) != 0)
            throw new InvalidDataException($"{path}: length is not a multiple of the block size");
        for (int i = 0; i < items.Count; i++)
            if (archive.Entries[i].Hash != items[i].Hash)
                throw new InvalidDataException($"{path}: entry {i} has hash {archive.Entries[i].Hash:x16}, expected {items[i].Hash:x16} ({items[i].Name})");

        try
        {
            Parallel.ForEach(encodedChecks, check =>
            {
                QarEntryInfo entry = archive.Entries[check.Index];
                if (Md5Key.Of(archive.ReadContent(entry)) != check.Md5)
                    throw new InvalidDataException($"{path}: {items[check.Index].Name} does not decode to the content that was written");
            });
        }
        catch (AggregateException e)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.Flatten().InnerExceptions[0]).Throw();
        }
    }

    private static long Align(long value, int alignment) => (value + alignment - 1) / alignment * alignment;

    private static void PadTo(Stream output, int alignment)
    {
        long remainder = output.Position % alignment;
        if (remainder > 0)
        {
            Span<byte> zeros = stackalloc byte[(int)(alignment - remainder)];
            zeros.Clear();
            output.Write(zeros);
        }
    }
}
