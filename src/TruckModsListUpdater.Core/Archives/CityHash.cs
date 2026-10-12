using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace TruckModsListUpdater.Core.Archives;

/// <summary>
/// CityHash64 v1.0.3, ported from Google's reference implementation (MIT license, see
/// THIRD-PARTY-NOTICES.md). HashFS archives index their files by this hash of the path.
/// </summary>
/// <remarks>
/// SCS uses v1.0.x, which gives different results from the later v1.1 for most inputs, so
/// a generic CityHash library cannot be substituted.
/// </remarks>
public static class CityHash
{
    const ulong K0 = 0xc3a5c85c97cb3127UL;
    const ulong K1 = 0xb492b66fbe98f273UL;
    const ulong K2 = 0x9ae16a3b2f90404fUL;
    const ulong K3 = 0xc949d7c7509e6557UL;

    public static ulong CityHash64(string s) => CityHash64(Encoding.UTF8.GetBytes(s));

    public static ulong CityHash64(ReadOnlySpan<byte> s)
    {
        int len = s.Length;
        if (len <= 32)
            return len <= 16 ? HashLen0To16(s) : HashLen17To32(s);
        if (len <= 64)
            return HashLen33To64(s);

        // For strings over 64 bytes we hash the end first, and then as we
        // loop we keep 56 bytes of state: v, w, x, y, and z.
        ulong x = Fetch64(s, len - 40);
        ulong y = Fetch64(s, len - 16) + Fetch64(s, len - 56);
        ulong z = HashLen16(Fetch64(s, len - 48) + (ulong)len, Fetch64(s, len - 24));
        var v = WeakHashLen32WithSeeds(s, len - 64, (ulong)len, z);
        var w = WeakHashLen32WithSeeds(s, len - 32, y + K1, x);
        x = x * K1 + Fetch64(s, 0);

        // Decrease len to the nearest multiple of 64, and operate on 64-byte chunks.
        int remaining = (len - 1) & ~63;
        int p = 0;
        do
        {
            x = Rotate(x + y + v.A + Fetch64(s, p + 8), 37) * K1;
            y = Rotate(y + v.B + Fetch64(s, p + 48), 42) * K1;
            x ^= w.B;
            y += v.A + Fetch64(s, p + 40);
            z = Rotate(z + w.A, 33) * K1;
            v = WeakHashLen32WithSeeds(s, p, v.B * K1, x + w.A);
            w = WeakHashLen32WithSeeds(s, p + 32, z + w.B, y + Fetch64(s, p + 16));
            (z, x) = (x, z);
            p += 64;
            remaining -= 64;
        } while (remaining != 0);

        return HashLen16(HashLen16(v.A, w.A) + ShiftMix(y) * K1 + z, HashLen16(v.B, w.B) + x);
    }

    static ulong HashLen0To16(ReadOnlySpan<byte> s)
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
            uint y = a + ((uint)b << 8);
            uint z = (uint)len + ((uint)c << 2);
            return ShiftMix(y * K2 ^ z * K3) * K2;
        }
        return K2;
    }

    static ulong HashLen17To32(ReadOnlySpan<byte> s)
    {
        int len = s.Length;
        ulong a = Fetch64(s, 0) * K1;
        ulong b = Fetch64(s, 8);
        ulong c = Fetch64(s, len - 8) * K2;
        ulong d = Fetch64(s, len - 16) * K0;
        return HashLen16(Rotate(a - b, 43) + Rotate(c, 30) + d, a + Rotate(b ^ K3, 20) - c + (ulong)len);
    }

    static ulong HashLen33To64(ReadOnlySpan<byte> s)
    {
        int len = s.Length;
        ulong z = Fetch64(s, 24);
        ulong a = Fetch64(s, 0) + ((ulong)len + Fetch64(s, len - 16)) * K0;
        ulong b = Rotate(a + z, 52);
        ulong c = Rotate(a, 37);
        a += Fetch64(s, 8);
        c += Rotate(a, 7);
        a += Fetch64(s, 16);
        ulong vf = a + z;
        ulong vs = b + Rotate(a, 31) + c;
        a = Fetch64(s, 16) + Fetch64(s, len - 32);
        z = Fetch64(s, len - 8);
        b = Rotate(a + z, 52);
        c = Rotate(a, 37);
        a += Fetch64(s, len - 24);
        c += Rotate(a, 7);
        a += Fetch64(s, len - 16);
        ulong wf = a + z;
        ulong ws = b + Rotate(a, 31) + c;
        ulong r = ShiftMix((vf + ws) * K2 + (wf + vs) * K0);
        return ShiftMix(r * K0 + vs) * K2;
    }

    static (ulong A, ulong B) WeakHashLen32WithSeeds(ReadOnlySpan<byte> s, int p, ulong a, ulong b) =>
        WeakHashLen32WithSeeds(Fetch64(s, p), Fetch64(s, p + 8), Fetch64(s, p + 16), Fetch64(s, p + 24), a, b);

    static (ulong A, ulong B) WeakHashLen32WithSeeds(ulong w, ulong x, ulong y, ulong z, ulong a, ulong b)
    {
        a += w;
        b = Rotate(b + a + z, 21);
        ulong c = a;
        a += x;
        a += y;
        b += Rotate(a, 44);
        return (a + z, b + c);
    }

    // Hash128to64 from city.h.
    static ulong HashLen16(ulong u, ulong v)
    {
        const ulong kMul = 0x9ddfea08eb382d69UL;
        ulong a = (u ^ v) * kMul;
        a ^= a >> 47;
        ulong b = (v ^ a) * kMul;
        b ^= b >> 47;
        b *= kMul;
        return b;
    }

    static ulong ShiftMix(ulong val) => val ^ (val >> 47);
    static ulong Rotate(ulong val, int shift) => shift == 0 ? val : BitOperations.RotateRight(val, shift);
    static ulong Fetch64(ReadOnlySpan<byte> s, int p) => BinaryPrimitives.ReadUInt64LittleEndian(s[p..]);
    static uint Fetch32(ReadOnlySpan<byte> s, int p) => BinaryPrimitives.ReadUInt32LittleEndian(s[p..]);
}
