// Read-only QAR archive: index, raw entry bytes and decoded content
using System.Buffers.Binary;
using System.IO.Compression;
using Microsoft.Win32.SafeHandles;
using DMM.Core.Hashing;

namespace DMM.Core.Qar;

/// <summary>One entry from a QAR index. Offset is the entry header position in the archive.</summary>
public readonly record struct QarEntryInfo(ulong Hash, long Offset, uint StoredSize, uint UncompressedSize, UInt128 Md5)
{
    public bool Compressed => StoredSize != UncompressedSize;
    public int RawLength => QarCipher.EntryHeaderSize + (int)StoredSize;
}

/// <summary>
/// QAR reader. Reads go through RandomAccess on one handle, so any number of threads can read entries at once.
/// </summary>
public sealed class QarArchive : IDisposable
{
    private const uint LuaMagic8 = 0xA0F8EFE6;
    private const uint LuaMagic16 = 0xE3F8EFE6;

    private readonly SafeFileHandle handle;
    private readonly Dictionary<ulong, QarEntryInfo> byHash;

    public string Path { get; }
    public uint Flags { get; }
    public int BlockShift => QarCipher.BlockShift(Flags);
    public long Length { get; }
    public IReadOnlyList<QarEntryInfo> Entries { get; }

    private QarArchive(string path, SafeFileHandle handle, uint flags, long length, List<QarEntryInfo> entries)
    {
        Path = path;
        this.handle = handle;
        Flags = flags;
        Length = length;
        Entries = entries;
        byHash = new Dictionary<ulong, QarEntryInfo>(entries.Count);
        foreach (QarEntryInfo entry in entries)
            byHash.TryAdd(entry.Hash, entry);
    }

    public static QarArchive Open(string path)
    {
        SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
        try
        {
            long length = RandomAccess.GetLength(handle);
            if (length < QarCipher.HeaderSize)
                throw new InvalidDataException($"{path}: too small for a QAR archive");

            Span<byte> header = stackalloc byte[QarCipher.HeaderSize];
            RandomAccess.Read(handle, header, 0);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != QarCipher.Magic)
                throw new InvalidDataException($"{path}: not a QAR archive");
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]) ^ QarCipher.Mask1;
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]) ^ QarCipher.Mask2;
            int shift = QarCipher.BlockShift(flags);
            if (QarCipher.HeaderSize + 8L * count > length)
                throw new InvalidDataException($"{path}: entry count {count} does not fit the file");

            byte[] sectionBytes = new byte[8 * count];
            RandomAccess.Read(handle, sectionBytes, QarCipher.HeaderSize);
            QarCipher.CryptSections(sectionBytes);

            var entries = new List<QarEntryInfo>((int)count);
            Span<byte> entryHeader = stackalloc byte[QarCipher.EntryHeaderSize];
            for (int i = 0; i < count; i++)
            {
                ulong section = BinaryPrimitives.ReadUInt64LittleEndian(sectionBytes.AsSpan(i * 8));
                long offset = (long)(section >> 40) << shift;
                if (offset + QarCipher.EntryHeaderSize > length)
                    throw new InvalidDataException($"{path}: entry {i} header at {offset} is past the end of the file");
                RandomAccess.Read(handle, entryHeader, offset);
                QarEntryInfo entry = ParseEntryHeader(entryHeader, offset);
                if (entry.StoredSize > int.MaxValue - QarCipher.EntryHeaderSize || offset + entry.RawLength > length)
                    throw new InvalidDataException($"{path}: entry {entry.Hash:x16} data is past the end of the file");
                entries.Add(entry);
            }
            return new QarArchive(path, handle, flags, length, entries);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static QarEntryInfo ParseEntryHeader(ReadOnlySpan<byte> h, long offset)
    {
        ulong hash = BinaryPrimitives.ReadUInt64LittleEndian(h) ^ QarCipher.HashMask;
        uint stored = BinaryPrimitives.ReadUInt32LittleEndian(h[8..]) ^ QarCipher.Mask2;
        uint uncompressed = BinaryPrimitives.ReadUInt32LittleEndian(h[12..]) ^ QarCipher.Mask3;
        Span<byte> md5 = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(md5, BinaryPrimitives.ReadUInt32LittleEndian(h[16..]) ^ QarCipher.Mask4);
        BinaryPrimitives.WriteUInt32LittleEndian(md5[4..], BinaryPrimitives.ReadUInt32LittleEndian(h[20..]) ^ QarCipher.Mask1);
        BinaryPrimitives.WriteUInt32LittleEndian(md5[8..], BinaryPrimitives.ReadUInt32LittleEndian(h[24..]) ^ QarCipher.Mask1);
        BinaryPrimitives.WriteUInt32LittleEndian(md5[12..], BinaryPrimitives.ReadUInt32LittleEndian(h[28..]) ^ QarCipher.Mask2);
        return new QarEntryInfo(hash, offset, stored, uncompressed, Md5Key.From(md5));
    }

    public bool TryGetEntry(ulong hash, out QarEntryInfo entry) => byHash.TryGetValue(hash, out entry);

    public bool Contains(ulong hash) => byHash.ContainsKey(hash);

    /// <summary>Copies the raw entry to a stream in chunks.</summary>
    public void CopyRaw(QarEntryInfo entry, Stream output, byte[] buffer)
    {
        long position = entry.Offset;
        long remaining = entry.RawLength;
        while (remaining > 0)
        {
            int chunk = (int)Math.Min(buffer.Length, remaining);
            ReadExactly(buffer.AsSpan(0, chunk), position);
            output.Write(buffer, 0, chunk);
            position += chunk;
            remaining -= chunk;
        }
    }

    /// <summary>Decoded file content, as GzsTool QarEntry.ReadData produces it (decrypted, lua key removed, decompressed).</summary>
    public byte[] ReadContent(QarEntryInfo entry)
    {
        byte[] data = new byte[entry.StoredSize];
        ReadExactly(data, entry.Offset + QarCipher.EntryHeaderSize);
        QarCipher.CryptData(data, (uint)(entry.Hash & 0xFFFFFFFF));

        if (data.Length >= 8)
        {
            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(data);
            int keyedHeader = magic == LuaMagic8 ? 8 : magic == LuaMagic16 ? 16 : 0;
            if (keyedHeader > 0 && data.Length >= keyedHeader)
            {
                uint key = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
                data = data[keyedHeader..];
                QarCipher.DecryptKeyed(data, key);
            }
        }

        if (entry.Compressed)
            data = Inflate(data, entry.UncompressedSize);
        return data;
    }

    private static byte[] Inflate(byte[] data, uint expectedSize)
    {
        using var input = new MemoryStream(data, writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream((int)Math.Min(expectedSize, 256u << 20));
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private void ReadExactly(Span<byte> destination, long offset)
    {
        while (destination.Length > 0)
        {
            int read = RandomAccess.Read(handle, destination, offset);
            if (read <= 0)
                throw new EndOfStreamException($"{Path}: unexpected end of file at {offset}");
            destination = destination[read..];
            offset += read;
        }
    }

    public void Dispose() => handle.Dispose();
}
