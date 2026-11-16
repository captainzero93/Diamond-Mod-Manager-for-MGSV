// FPK/FPKD archive read and write
using System.Buffers.Binary;
using DMM.Core.Hashing;

namespace DMM.Core.Fpk;

/// <summary>
/// One FPK entry. Name is the extracted file name GzsTool gives it ("Assets\tpp\...", drive prefix removed).
/// EncryptedName is set when the stored name is encrypted and could not be resolved; it is written back unchanged with the original Md5.
/// </summary>
public sealed class FpkEntry
{
    public required string Name { get; init; }
    public required byte[] Data { get; init; }
    public required byte[] Md5 { get; init; }
    public byte[]? EncryptedName { get; init; }

    /// <summary>"/Assets/..." path written into a rebuilt FPK (SnakeBite Tools.ToQarPath of the extracted name).</summary>
    public string QarPath => FoxPath.ToQarPath(Name);
}

/// <summary>Layout from GzsTool.Core Fpk/FpkFile.cs, FpkEntry.cs, FpkString.cs.</summary>
public sealed class FpkArchive
{
    private const uint MagicFoxf = 0x66786f66;
    private const int HeaderSize = 48;
    private const int EntrySize = 48;
    private const int ReferenceSize = 16;

    public bool IsFpkd { get; init; }
    public List<FpkEntry> Entries { get; init; } = [];
    public List<string> References { get; init; } = [];

    public static bool IsFpkdName(string fileName) => fileName.EndsWith(".fpkd", StringComparison.OrdinalIgnoreCase);

    public static FpkArchive Read(ReadOnlySpan<byte> data, FpkDictionary dictionary)
    {
        if (data.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(data) != MagicFoxf)
            throw new InvalidDataException("not an FPK archive");

        uint rawFiles = BinaryPrimitives.ReadUInt32LittleEndian(data[36..]);
        uint rawReferences = BinaryPrimitives.ReadUInt32LittleEndian(data[40..]);
        if (HeaderSize + (long)rawFiles * EntrySize + (long)rawReferences * ReferenceSize > data.Length)
            throw new InvalidDataException("FPK index is larger than the file");
        int fileCount = (int)rawFiles, referenceCount = (int)rawReferences;

        var archive = new FpkArchive { IsFpkd = data[6] == 0x64 };
        long totalData = 0;
        for (int i = 0; i < fileCount; i++)
        {
            ReadOnlySpan<byte> e = data.Slice(HeaderSize + i * EntrySize, EntrySize);
            uint dataOffset = BinaryPrimitives.ReadUInt32LittleEndian(e);
            int dataSize = BinaryPrimitives.ReadInt32LittleEndian(e[8..]);
            byte[] storedName = ReadString(data, e[16..]);
            byte[] md5 = e[32..48].ToArray();
            if (dataSize < 0 || dataOffset + (long)dataSize > data.Length)
                throw new InvalidDataException($"FPK entry {i} data is outside the file");
            // entries pointing at the same data again and again would copy it out without limit
            if ((totalData += dataSize) > 4L * data.Length)
                throw new InvalidDataException("FPK entries overlap far more than any real pack");

            string value = AnsiText.GetString(storedName);
            byte[]? encryptedName = null;
            if (Md5Key.Of(storedName) != Md5Key.From(md5))
            {
                // GzsTool FpkString.ResolveString
                encryptedName = storedName;
                if (dictionary.TryGetName(md5, out string resolved))
                {
                    value = resolved;
                    encryptedName = null;
                }
                else
                {
                    int dot = value.LastIndexOf('.');
                    value = Convert.ToHexString(md5) + (dot == -1 ? "" : value[dot..]);
                }
            }

            string name = EntryFileName(value);
            byte[] content = data.Slice((int)dataOffset, dataSize).ToArray();
            if (content.Length > 0 && (content[0] == 0x1B || content[0] == 0x1C) && TryDecryptData(content, name, out byte[] decrypted))
                content = decrypted;

            archive.Entries.Add(new FpkEntry { Name = name, Data = content, Md5 = md5, EncryptedName = encryptedName });
        }

        int referenceStart = HeaderSize + fileCount * EntrySize;
        for (int i = 0; i < referenceCount; i++)
            archive.References.Add(AnsiText.GetString(ReadString(data, data.Slice(referenceStart + i * ReferenceSize, ReferenceSize))));
        return archive;
    }

    private static byte[] ReadString(ReadOnlySpan<byte> file, ReadOnlySpan<byte> fpkString)
    {
        int offset = BinaryPrimitives.ReadInt32LittleEndian(fpkString);
        int length = BinaryPrimitives.ReadInt32LittleEndian(fpkString[8..]);
        if (offset < 0 || length < 0 || offset + (long)length > file.Length)
            throw new InvalidDataException("FPK string is outside the file");
        return file.Slice(offset, length).ToArray();
    }

    // GzsTool FpkEntry.GetFpkEntryFileName: backslashes, no leading slash, drive prefix ("Z:") removed
    private static string EntryFileName(string value)
    {
        string fileName = value.Replace('/', '\\').TrimStart('\\');
        int colon = fileName.IndexOf(':');
        return colon == -1 ? fileName : fileName[(colon + 1)..];
    }

    // GzsTool FpkEntry.TryDecryptData
    private static bool TryDecryptData(byte[] data, string entryName, out byte[] result)
    {
        result = new byte[data.Length - 1];
        string fileName = entryName[(entryName.LastIndexOfAny(['\\', '/']) + 1)..].ToLowerInvariant();
        ulong hash = FoxPath.HashFileNameLegacy(fileName, false);
        Span<byte> key = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(key, ~hash);
        for (int i = 0; i < data.Length - 1; i++)
        {
            key[i % 8] ^= data[i + 1];
            result[i] = key[i % 8];
        }
        if (result.Length == 0 || result[^1] != 0)
            return false;
        Array.Resize(ref result, result.Length - 1);
        return true;
    }

    /// <summary>Writes the archive like GzsTool FpkFile.Write. Entries keep their order.</summary>
    public byte[] Write()
    {
        using var output = new MemoryStream();
        int indexSize = HeaderSize + EntrySize * Entries.Count + ReferenceSize * References.Count;
        output.SetLength(indexSize);
        output.Position = indexSize;

        var entryStrings = new (int Offset, int Length)[Entries.Count];
        var entryMd5 = new byte[Entries.Count][];
        for (int i = 0; i < Entries.Count; i++)
        {
            FpkEntry entry = Entries[i];
            byte[] name = entry.EncryptedName ?? AnsiText.GetBytes(entry.QarPath);
            entryMd5[i] = entry.EncryptedName != null ? entry.Md5 : Md5Key.ToBytes(Md5Key.Of(name));
            entryStrings[i] = ((int)output.Position, name.Length);
            output.Write(name);
            output.WriteByte(0);
        }
        var referenceStrings = new (int Offset, int Length)[References.Count];
        for (int i = 0; i < References.Count; i++)
        {
            byte[] name = AnsiText.GetBytes(References[i]);
            referenceStrings[i] = ((int)output.Position, name.Length);
            output.Write(name);
            output.WriteByte(0);
        }
        Pad16(output);

        var dataPositions = new (uint Offset, int Size)[Entries.Count];
        for (int i = 0; i < Entries.Count; i++)
        {
            dataPositions[i] = ((uint)output.Position, Entries[i].Data.Length);
            output.Write(Entries[i].Data);
            Pad16(output);
        }
        uint fileSize = (uint)output.Position;

        byte[] bytes = output.ToArray();
        Span<byte> h = bytes;
        BinaryPrimitives.WriteUInt32LittleEndian(h, MagicFoxf);
        BinaryPrimitives.WriteUInt16LittleEndian(h[4..], 0x6B70); // pk
        h[6] = IsFpkd ? (byte)0x64 : (byte)0x00;
        h[7] = 0x77; // w
        BinaryPrimitives.WriteUInt16LittleEndian(h[8..], 0x6E69); // in
        BinaryPrimitives.WriteUInt32LittleEndian(h[10..], fileSize);
        BinaryPrimitives.WriteUInt32LittleEndian(h[32..], 2);
        BinaryPrimitives.WriteInt32LittleEndian(h[36..], Entries.Count);
        BinaryPrimitives.WriteInt32LittleEndian(h[40..], References.Count);

        for (int i = 0; i < Entries.Count; i++)
        {
            Span<byte> e = h.Slice(HeaderSize + i * EntrySize, EntrySize);
            BinaryPrimitives.WriteUInt32LittleEndian(e, dataPositions[i].Offset);
            BinaryPrimitives.WriteInt32LittleEndian(e[8..], dataPositions[i].Size);
            BinaryPrimitives.WriteInt32LittleEndian(e[16..], entryStrings[i].Offset);
            BinaryPrimitives.WriteInt32LittleEndian(e[24..], entryStrings[i].Length);
            entryMd5[i].CopyTo(e[32..]);
        }
        int referenceStart = HeaderSize + Entries.Count * EntrySize;
        for (int i = 0; i < References.Count; i++)
        {
            Span<byte> r = h.Slice(referenceStart + i * ReferenceSize, ReferenceSize);
            BinaryPrimitives.WriteInt32LittleEndian(r, referenceStrings[i].Offset);
            BinaryPrimitives.WriteInt32LittleEndian(r[8..], referenceStrings[i].Length);
        }
        return bytes;
    }

    private static void Pad16(Stream output)
    {
        long remainder = output.Position % 16;
        for (long i = remainder; i is > 0 and < 16; i++)
            output.WriteByte(0);
    }
}
