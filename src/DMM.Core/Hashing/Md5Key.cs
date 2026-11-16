// MD5 digest as a UInt128 key and hex string
using System.Security.Cryptography;

namespace DMM.Core.Hashing;

public static class Md5Key
{
    public static UInt128 From(ReadOnlySpan<byte> md5) => new(BitConverter.ToUInt64(md5[8..]), BitConverter.ToUInt64(md5));

    public static UInt128 Of(ReadOnlySpan<byte> data) => From(MD5.HashData(data));

    public static UInt128 OfFile(string path)
    {
        // shared for writing too: hashing must never stop Steam or the game from replacing the file
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
        return From(MD5.HashData(stream));
    }

    public static void Write(UInt128 key, Span<byte> destination)
    {
        BitConverter.TryWriteBytes(destination, (ulong)key);
        BitConverter.TryWriteBytes(destination[8..], (ulong)(key >> 64));
    }

    public static byte[] ToBytes(UInt128 key)
    {
        byte[] bytes = new byte[16];
        Write(key, bytes);
        return bytes;
    }

    /// <summary>Uppercase hex, the form SnakeBite stores (Tools.GetMd5Hash).</summary>
    public static string ToHex(UInt128 key) => Convert.ToHexString(ToBytes(key));

    public static UInt128 FromHex(string hex) => From(Convert.FromHexString(hex));
}
