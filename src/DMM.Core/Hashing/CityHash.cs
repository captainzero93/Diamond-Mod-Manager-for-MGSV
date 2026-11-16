// CityHash64 v1.0 as used by Fox Engine path hashing
using System.Buffers.Binary;
using System.Numerics;

namespace DMM.Core.Hashing;

// Matches Atvaark's CityHash port (CityHash.dll 0.1.2) that GzsTool and SnakeBite use. That port follows
// CityHash 1.0.x (HashLen0To16 / HashLen17To32 without the 1.1 mul variants), which is what the game hashes match.
public static class CityHash
{
    private const ulong K0 = 0xc3a5c85c97cb3127;
    private const ulong K1 = 0xb492b66fbe98f273;
    private const ulong K2 = 0x9ae16a3b2f90404f;
    private const ulong K3 = 0xc949d7c7509e6557;
    private const ulong KMul = 0x9ddfea08eb382d69;

    public static ulong CityHash64WithSeeds(ReadOnlySpan<byte> s, ulong seed0, ulong seed1)
    {
        return HashLen16(CityHash64(s) - seed0, seed1);
    }

    public static ulong CityHash64(ReadOnlySpan<byte> s)
    {
        int len = s.Length;
        if (len <= 32)
            return len <= 16 ? HashLen0To16(s) : HashLen17To32(s);
        if (len <= 64)
            return HashLen33To64(s);

        ulong x = Fetch64(s, len - 40);
        ulong y = Fetch64(s, len - 16) + Fetch64(s, len - 56);
        ulong z = HashLen16(Fetch64(s, len - 48) + (ulong)len, Fetch64(s, len - 24));
        (ulong vLow, ulong vHigh) = WeakHashLen32WithSeeds(s, len - 64, (ulong)len, z);
        (ulong wLow, ulong wHigh) = WeakHashLen32WithSeeds(s, len - 32, y + K1, x);
        x = x * K1 + Fetch64(s, 0);

        int remaining = (len - 1) & ~63;
        int offset = 0;
        do
        {
            x = BitOperations.RotateRight(x + y + vLow + Fetch64(s, offset + 8), 37) * K1;
            y = BitOperations.RotateRight(y + vHigh + Fetch64(s, offset + 48), 42) * K1;
            x ^= wHigh;
            y += vLow + Fetch64(s, offset + 40);
            z = BitOperations.RotateRight(z + wLow, 33) * K1;
            (vLow, vHigh) = WeakHashLen32WithSeeds(s, offset, vHigh * K1, x + wLow);
            (wLow, wHigh) = WeakHashLen32WithSeeds(s, offset + 32, z + wHigh, y + Fetch64(s, offset + 16));
            (z, x) = (x, z);
            offset += 64;
            remaining -= 64;
        }
        while (remaining != 0);

        return HashLen16(HashLen16(vLow, wLow) + ShiftMix(y) * K1 + z, HashLen16(vHigh, wHigh) + x);
    }

    private static ulong Fetch64(ReadOnlySpan<byte> s, int index) => BinaryPrimitives.ReadUInt64LittleEndian(s[index..]);

    private static uint Fetch32(ReadOnlySpan<byte> s, int index) => BinaryPrimitives.ReadUInt32LittleEndian(s[index..]);

    private static ulong ShiftMix(ulong value) => value ^ (value >> 47);

    private static ulong HashLen16(ulong u, ulong v)
    {
        ulong a = (u ^ v) * KMul;
        a ^= a >> 47;
        ulong b = (v ^ a) * KMul;
        b ^= b >> 47;
        return b * KMul;
    }

    private static ulong HashLen0To16(ReadOnlySpan<byte> s)
    {
        int len = s.Length;
        if (len > 8)
        {
            ulong a = Fetch64(s, 0);
            ulong b = Fetch64(s, len - 8);
            return HashLen16(a, BitOperations.RotateRight(b + (ulong)len, len)) ^ b;
        }
        if (len >= 4)
        {
            ulong a = Fetch32(s, 0);
            return HashLen16((ulong)len + (a << 3), Fetch32(s, len - 4));
        }
        if (len > 0)
        {
            byte a = s[0];
            byte b = s[len >> 1];
            byte c = s[len - 1];
            uint y = (uint)(a + (b << 8));
            uint z = (uint)(len + (c << 2));
            return ShiftMix(y * K2 ^ z * K3) * K2;
        }
        return K2;
    }

    private static ulong HashLen17To32(ReadOnlySpan<byte> s)
    {
        ulong len = (ulong)s.Length;
        ulong a = Fetch64(s, 0) * K1;
        ulong b = Fetch64(s, 8);
        ulong c = Fetch64(s, s.Length - 8) * K2;
        ulong d = Fetch64(s, s.Length - 16) * K0;
        return HashLen16(BitOperations.RotateRight(a - b, 43) + BitOperations.RotateRight(c, 30) + d,
            a + BitOperations.RotateRight(b ^ K3, 20) - c + len);
    }

    private static ulong HashLen33To64(ReadOnlySpan<byte> s)
    {
        int len = s.Length;
        ulong z = Fetch64(s, 24);
        ulong a = Fetch64(s, 0) + ((ulong)len + Fetch64(s, len - 16)) * K0;
        ulong b = BitOperations.RotateRight(a + z, 52);
        ulong c = BitOperations.RotateRight(a, 37);
        a += Fetch64(s, 8);
        c += BitOperations.RotateRight(a, 7);
        a += Fetch64(s, 16);
        ulong vf = a + z;
        ulong vs = b + BitOperations.RotateRight(a, 31) + c;
        a = Fetch64(s, 16) + Fetch64(s, len - 32);
        z = Fetch64(s, len - 8);
        b = BitOperations.RotateRight(a + z, 52);
        c = BitOperations.RotateRight(a, 37);
        a += Fetch64(s, len - 24);
        c += BitOperations.RotateRight(a, 7);
        a += Fetch64(s, len - 16);
        ulong wf = a + z;
        ulong ws = b + BitOperations.RotateRight(a, 31) + c;
        ulong r = ShiftMix((vf + ws) * K2 + (wf + vs) * K0);
        return ShiftMix(r * K0 + vs) * K2;
    }

    private static (ulong Low, ulong High) WeakHashLen32WithSeeds(ReadOnlySpan<byte> s, int offset, ulong a, ulong b)
    {
        ulong w = Fetch64(s, offset);
        ulong x = Fetch64(s, offset + 8);
        ulong y = Fetch64(s, offset + 16);
        ulong z = Fetch64(s, offset + 24);
        a += w;
        b = BitOperations.RotateRight(b + a + z, 21);
        ulong c = a;
        a += x;
        a += y;
        b += BitOperations.RotateRight(a, 44);
        return (a + z, b + c);
    }
}
