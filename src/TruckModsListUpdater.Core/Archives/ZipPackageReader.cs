using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace TruckModsListUpdater.Core.Archives;

/// <summary>
/// Reads files from ZIP-based mods (.zip and ZIP .scs) using only the central directory.
/// </summary>
/// <remarks>
/// Many mods "protect" their ZIPs by corrupting the local file headers (wrong compression method,
/// bogus name lengths); the game ignores those and so do we. The data offset is taken from the
/// local header when it looks sane, otherwise from the central directory. Protected ZIPs also set
/// the "encrypted" flag and fake CRCs without encrypting anything, so both are only hints.
/// </remarks>
public sealed class ZipPackageReader : IPackageReader
{
    const uint EocdSignature = 0x06054b50;
    const uint Zip64LocatorSignature = 0x07064b50;
    const uint Zip64EocdSignature = 0x06064b50;
    const uint CentralSignature = 0x02014b50;
    const uint LocalSignature = 0x04034b50;

    readonly FileStream _stream;
    readonly Dictionary<string, CentralEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public PackageKind Kind => PackageKind.Zip;
    public IReadOnlyCollection<string> EntryNames => _entries.Keys;

    sealed record CentralEntry(string Name, ushort Method, uint Crc, long CompressedSize, long Size, long LocalOffset, int NameLength);

    public ZipPackageReader(string path)
    {
        _stream = PackageReader.OpenShared(path);
        try
        {
            ReadCentralDirectory();
        }
        catch (Exception e) when (e is EndOfStreamException or ArgumentOutOfRangeException or IOException)
        {
            _stream.Dispose();
            throw new PackageFormatException($"Could not read ZIP directory of {Path.GetFileName(path)}: {e.Message}", e);
        }
        catch
        {
            _stream.Dispose();
            throw;
        }
    }

    void ReadCentralDirectory()
    {
        // The end-of-central-directory record is in the last 64 KiB + 22 bytes.
        long tailLength = Math.Min(_stream.Length, 65535 + 22);
        var tail = ReadAt(_stream.Length - tailLength, (int)tailLength);
        int eocd = -1;
        for (int i = tail.Length - 22; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) == EocdSignature) { eocd = i; break; }
        }
        if (eocd < 0) throw new PackageFormatException("ZIP end of central directory not found.");

        var e = tail.AsSpan(eocd);
        long count = BinaryPrimitives.ReadUInt16LittleEndian(e[10..]);
        long cdSize = BinaryPrimitives.ReadUInt32LittleEndian(e[12..]);
        long cdOffset = BinaryPrimitives.ReadUInt32LittleEndian(e[16..]);

        // ZIP64: a locator just before the EOCD points to the ZIP64 EOCD record.
        if (eocd >= 20 && BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(eocd - 20)) == Zip64LocatorSignature)
        {
            long z64Offset = (long)BinaryPrimitives.ReadUInt64LittleEndian(tail.AsSpan(eocd - 20 + 8));
            var z = ReadAt(z64Offset, 56);
            if (BinaryPrimitives.ReadUInt32LittleEndian(z) == Zip64EocdSignature)
            {
                count = (long)BinaryPrimitives.ReadUInt64LittleEndian(z.AsSpan(32));
                cdSize = (long)BinaryPrimitives.ReadUInt64LittleEndian(z.AsSpan(40));
                cdOffset = (long)BinaryPrimitives.ReadUInt64LittleEndian(z.AsSpan(48));
            }
        }
        if (cdOffset + cdSize > _stream.Length || cdSize > int.MaxValue)
            throw new PackageFormatException("ZIP central directory lies outside the file.");

        var cd = ReadAt(cdOffset, (int)cdSize);
        int p = 0;
        for (long n = 0; n < count && p + 46 <= cd.Length; n++)
        {
            var h = cd.AsSpan(p);
            if (BinaryPrimitives.ReadUInt32LittleEndian(h) != CentralSignature) break;
            ushort method = BinaryPrimitives.ReadUInt16LittleEndian(h[10..]);
            uint crc = BinaryPrimitives.ReadUInt32LittleEndian(h[16..]);
            long csize = BinaryPrimitives.ReadUInt32LittleEndian(h[20..]);
            long usize = BinaryPrimitives.ReadUInt32LittleEndian(h[24..]);
            int nameLen = BinaryPrimitives.ReadUInt16LittleEndian(h[28..]);
            int extraLen = BinaryPrimitives.ReadUInt16LittleEndian(h[30..]);
            int commentLen = BinaryPrimitives.ReadUInt16LittleEndian(h[32..]);
            long localOffset = BinaryPrimitives.ReadUInt32LittleEndian(h[42..]);
            if (p + 46 + nameLen + extraLen > cd.Length) break;

            var name = Encoding.UTF8.GetString(h.Slice(46, nameLen));
            ApplyZip64Extra(h.Slice(46 + nameLen, extraLen), ref usize, ref csize, ref localOffset);

            var key = PackageReader.Normalize(name);
            if (key.Length > 0 && !key.EndsWith('/'))
                _entries.TryAdd(key, new CentralEntry(key, method, crc, csize, usize, localOffset, nameLen));
            p += 46 + nameLen + extraLen + commentLen;
        }
    }

    static void ApplyZip64Extra(ReadOnlySpan<byte> extra, ref long usize, ref long csize, ref long offset)
    {
        while (extra.Length >= 4)
        {
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(extra);
            int size = BinaryPrimitives.ReadUInt16LittleEndian(extra[2..]);
            if (4 + size > extra.Length) return;
            if (id == 0x0001)
            {
                var d = extra.Slice(4, size);
                if (usize == uint.MaxValue && d.Length >= 8) { usize = (long)BinaryPrimitives.ReadUInt64LittleEndian(d); d = d[8..]; }
                if (csize == uint.MaxValue && d.Length >= 8) { csize = (long)BinaryPrimitives.ReadUInt64LittleEndian(d); d = d[8..]; }
                if (offset == uint.MaxValue && d.Length >= 8) offset = (long)BinaryPrimitives.ReadUInt64LittleEndian(d);
                return;
            }
            extra = extra[(4 + size)..];
        }
    }

    public bool TryReadFile(string path, out byte[] data)
    {
        data = [];
        if (!_entries.TryGetValue(PackageReader.Normalize(path), out var entry)) return false;
        if (entry.Size > PackageReader.MaxEntrySize || entry.CompressedSize > PackageReader.MaxEntrySize) return false;

        // Protected ZIPs also fake CRCs, so a CRC match only decides between candidates; otherwise
        // the first candidate (most likely offset) that decompresses to the right size wins.
        byte[]? fallback = null;
        foreach (var dataOffset in CandidateDataOffsets(entry).Distinct())
        {
            if (dataOffset < 0 || dataOffset + entry.CompressedSize > _stream.Length) continue;
            // Protected ZIPs may lie about the method too; deflate is by far the most common.
            foreach (var method in entry.Method is 0 or 8 ? new[] { entry.Method } : new ushort[] { 8, 0 })
            {
                if (!TryExtract(entry, dataOffset, method, out var candidate)) continue;
                if (Crc32.Compute(candidate) == entry.Crc)
                {
                    data = candidate;
                    return true;
                }
                fallback ??= candidate;
            }
        }
        data = fallback ?? [];
        return fallback != null;
    }

    IEnumerable<long> CandidateDataOffsets(CentralEntry entry)
    {
        var local = ReadAt(entry.LocalOffset, 30);
        int localNameLen = BinaryPrimitives.ReadUInt16LittleEndian(local.AsSpan(26));
        int localExtraLen = BinaryPrimitives.ReadUInt16LittleEndian(local.AsSpan(28));
        bool sane = BinaryPrimitives.ReadUInt32LittleEndian(local) == LocalSignature && localNameLen == entry.NameLength;
        if (sane) yield return entry.LocalOffset + 30 + localNameLen + localExtraLen;
        yield return entry.LocalOffset + 30 + entry.NameLength + localExtraLen;
        yield return entry.LocalOffset + 30 + entry.NameLength;
    }

    bool TryExtract(CentralEntry entry, long dataOffset, ushort method, out byte[] data)
    {
        data = [];
        try
        {
            var raw = ReadAt(dataOffset, (int)entry.CompressedSize);
            if (method == 0)
            {
                data = raw;
            }
            else
            {
                using var d = new DeflateStream(new MemoryStream(raw), CompressionMode.Decompress);
                data = new byte[entry.Size];
                d.ReadExactly(data);
            }
            return data.Length == entry.Size;
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException or IOException)
        {
            return false;
        }
    }

    byte[] ReadAt(long offset, int length)
    {
        var buf = new byte[length];
        lock (_stream)
        {
            _stream.Position = offset;
            _stream.ReadExactly(buf);
        }
        return buf;
    }

    public void Dispose() => _stream.Dispose();
}

/// <summary>Standard CRC-32 (IEEE 802.3), as used by ZIP.</summary>
static class Crc32
{
    static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }
}
