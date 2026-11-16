// QAR header masks and section, entry and lua ciphers
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace DMM.Core.Qar;

// Ciphers from GzsTool.Core Qar/QarFile.cs (DecryptSectionList) and Qar/QarEntry.cs (Decrypt1, Decrypt2).
// Retail 1.0.15.4 decrypts entry data relative to the entry start (0x14005a620), so stored entries are position independent.
public static class QarCipher
{
    public const uint Magic = 0x52415153; // SQAR
    public const uint Mask1 = 0x41441043;
    public const uint Mask2 = 0x11C22050;
    public const uint Mask3 = 0xD05608C3;
    public const uint Mask4 = 0x532C7319;
    public const ulong HashMask = 0x4144104341441043;
    public const int HeaderSize = 32;
    public const int EntryHeaderSize = 32;

    private static readonly uint[] SectionKeys = [0x41441043, 0x11C22050, 0xD05608C3, 0x532C7319];
    private static readonly uint[] DataKeys = [0xBB8ADEDB, 0x65229958, 0x08453206, 0x88121302, 0x4C344955, 0x2C02F10C, 0x4887F823, 0xF3818583];

    public static int BlockShift(uint flags) => (flags & 0x800) != 0 ? 12 : 10;

    /// <summary>Section table cipher. Applying it twice restores the input.</summary>
    public static void CryptSections(Span<byte> sections)
    {
        int count = sections.Length / 8;
        for (int i = 0; i < count; i++)
        {
            int lowOffset = i * 8;
            int highOffset = lowOffset + 4;
            Span<byte> low = sections.Slice(lowOffset, 4);
            Span<byte> high = sections.Slice(highOffset, 4);
            BinaryPrimitives.WriteUInt32LittleEndian(low, BinaryPrimitives.ReadUInt32LittleEndian(low) ^ SectionKeys[(i + lowOffset / 5) % 4]);
            BinaryPrimitives.WriteUInt32LittleEndian(high, BinaryPrimitives.ReadUInt32LittleEndian(high) ^ SectionKeys[(i + highOffset / 5) % 4]);
        }
    }

    /// <summary>
    /// Entry data cipher (GzsTool Decrypt1). <paramref name="startOffset"/> is the position of <paramref name="data"/>
    /// inside the entry's stored data and must be a multiple of 8 unless it is the final chunk. Applying it twice restores the input.
    /// </summary>
    public static void CryptData(Span<byte> data, uint hashLow, long startOffset = 0)
    {
        int blocks = data.Length / 8;
        Span<uint> words = MemoryMarshal.Cast<byte, uint>(data[..(blocks * 8)]);
        for (int i = 0; i < blocks; i++)
        {
            long offset = startOffset + i * 8L;
            int index = (int)(2 * ((hashLow + offset / 11) % 4));
            words[i * 2] ^= DataKeys[index];
            words[i * 2 + 1] ^= DataKeys[index + 1];
        }

        int remaining = data.Length % 8;
        long tailStart = startOffset + blocks * 8L;
        for (int i = 0; i < remaining; i++)
        {
            long offset = tailStart + i;
            int index = (int)(2 * ((hashLow + (offset - offset % 8) / 11) % 4));
            int byteIndex = (int)(offset % 8);
            uint key = byteIndex < 4 ? DataKeys[index] : DataKeys[index + 1];
            data[blocks * 8 + i] ^= (byte)(key >> (8 * (byteIndex % 4)));
        }
    }

    /// <summary>Lua payload cipher (GzsTool Decrypt2). Whole 4-byte words only; trailing bytes are left as they are.</summary>
    public static void DecryptKeyed(Span<byte> data, uint key)
    {
        uint currentKey = key | ((key ^ 25974) << 16);
        uint step = 278 * key;
        Span<uint> words = MemoryMarshal.Cast<byte, uint>(data[..(data.Length / 4 * 4)]);
        for (int i = 0; i < words.Length; i++)
        {
            words[i] ^= currentKey;
            currentKey = step + 48828125 * currentKey;
        }
    }
}
