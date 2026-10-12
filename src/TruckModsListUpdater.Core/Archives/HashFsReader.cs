using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace TruckModsListUpdater.Core.Archives;

/// <summary>
/// Reader for SCS HashFS archives ("SCS#" .scs files), versions 1 and 2. Files are found by
/// CityHash64 of their path without a leading slash (the root directory is the hash of "").
/// </summary>
/// <remarks>
/// v1 layout (little-endian):
///   header: "SCS#", u16 version, u16 salt, "CITY", u32 entry count, u64 entry table offset
///   entry:  u64 hash, u64 offset, u32 flags (1 = directory, 2 = zlib), u32 crc, u32 size, u32 compressed size
///   directory content: names separated by '\n', subdirectories prefixed with '*'
/// v2 layout (game 1.50+):
///   header: "SCS#", u16 version, u16 salt, "CITY", u32 entry count, u32 entry table length,
///           u32 metadata count, u32 metadata table length, u64 entry table offset, u64 metadata table offset, ...
///   entry table (zlib): u64 hash, u32 metadata index, u16 metadata count, u16 flags
///   metadata table (zlib, 4-byte blocks): per entry its chunk headers (u24 index, u8 type), then for
///           plain files (128) and directories (129): u32 compressed size (28 bits, 0x10 of the top byte =
///           zlib), u32 size (28 bits), u32 unknown, u32 offset in 16-byte blocks
///   directory content: u32 count, count × u8 name length, names; subdirectories prefixed with '/'
/// Packed textures (type 1) use GDeflate and are not supported; mods keep icons and manifests as plain files.
/// </remarks>
public sealed class HashFsReader : IPackageReader
{
    const int V1EntrySize = 32;
    const int V2EntrySize = 16;
    const byte ChunkPlain = 128;
    const byte ChunkDirectory = 129;

    readonly FileStream _stream;
    readonly Dictionary<ulong, Entry> _entries;
    readonly ushort _salt;

    public PackageKind Kind => PackageKind.HashFs;
    public ushort Version { get; }

    readonly record struct Entry(ulong Offset, bool IsDirectory, bool IsCompressed, uint Size, uint CompressedSize);

    HashFsReader(FileStream stream, ushort version, ushort salt, Dictionary<ulong, Entry> entries)
    {
        _stream = stream;
        Version = version;
        _salt = salt;
        _entries = entries;
    }

    public static HashFsReader Open(string path)
    {
        var fs = PackageReader.OpenShared(path);
        try
        {
            Span<byte> header = stackalloc byte[48];
            fs.ReadExactly(header[..12]);
            if (!header[..4].SequenceEqual("SCS#"u8)) throw new PackageFormatException("Not a HashFS archive.");
            ushort version = BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);
            ushort salt = BinaryPrimitives.ReadUInt16LittleEndian(header[6..]);
            if (!header[8..12].SequenceEqual("CITY"u8)) throw new PackageFormatException("Unsupported HashFS hash method.");

            var entries = version switch
            {
                1 => ReadV1Index(fs),
                2 => ReadV2Index(fs),
                _ => throw new PackageFormatException($"HashFS version {version} is not supported."),
            };
            return new HashFsReader(fs, version, salt, entries);
        }
        catch (Exception e) when (e is EndOfStreamException or InvalidDataException or ArgumentOutOfRangeException)
        {
            fs.Dispose();
            throw new PackageFormatException("HashFS archive index is damaged: " + e.Message, e);
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }

    static Dictionary<ulong, Entry> ReadV1Index(FileStream fs)
    {
        Span<byte> h = stackalloc byte[12];
        fs.ReadExactly(h);
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(h);
        ulong tableOffset = BinaryPrimitives.ReadUInt64LittleEndian(h[4..]);
        if ((ulong)count * V1EntrySize + tableOffset > (ulong)fs.Length)
            throw new PackageFormatException("HashFS entry table lies outside the file.");

        var table = ReadAt(fs, (long)tableOffset, (int)(count * V1EntrySize));
        var entries = new Dictionary<ulong, Entry>((int)count);
        for (int i = 0; i < count; i++)
        {
            var e = table.AsSpan(i * V1EntrySize, V1EntrySize);
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(e[16..]);
            entries.TryAdd(BinaryPrimitives.ReadUInt64LittleEndian(e), new Entry(
                BinaryPrimitives.ReadUInt64LittleEndian(e[8..]),
                (flags & 1) != 0,
                (flags & 2) != 0,
                BinaryPrimitives.ReadUInt32LittleEndian(e[24..]),
                BinaryPrimitives.ReadUInt32LittleEndian(e[28..])));
        }
        return entries;
    }

    static Dictionary<ulong, Entry> ReadV2Index(FileStream fs)
    {
        Span<byte> h = stackalloc byte[32];
        fs.ReadExactly(h);
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(h);
        uint entryTableLength = BinaryPrimitives.ReadUInt32LittleEndian(h[4..]);
        uint metadataTableLength = BinaryPrimitives.ReadUInt32LittleEndian(h[12..]);
        long entryTableStart = (long)BinaryPrimitives.ReadUInt64LittleEndian(h[16..]);
        long metadataTableStart = (long)BinaryPrimitives.ReadUInt64LittleEndian(h[24..]);
        if (entryTableStart + entryTableLength > fs.Length || metadataTableStart + metadataTableLength > fs.Length)
            throw new PackageFormatException("HashFS tables lie outside the file.");

        var table = Inflate(ReadAt(fs, entryTableStart, (int)entryTableLength));
        var metadata = Inflate(ReadAt(fs, metadataTableStart, (int)metadataTableLength));
        if (table.Length < count * V2EntrySize) throw new PackageFormatException("HashFS entry table is truncated.");

        var entries = new Dictionary<ulong, Entry>((int)count);
        for (int i = 0; i < count; i++)
        {
            var e = table.AsSpan(i * V2EntrySize, V2EntrySize);
            ulong hash = BinaryPrimitives.ReadUInt64LittleEndian(e);
            int metaPos = (int)BinaryPrimitives.ReadUInt32LittleEndian(e[8..]) * 4;
            int chunkCount = BinaryPrimitives.ReadUInt16LittleEndian(e[12..]);
            if (chunkCount == 0 || metaPos + chunkCount * 4 + 16 > metadata.Length) continue;

            byte type = metadata[metaPos + 3]; // first chunk decides what the entry is
            if (type is not (ChunkPlain or ChunkDirectory)) continue; // packed textures etc.

            var m = metadata.AsSpan(metaPos + chunkCount * 4, 16);
            uint compressedWord = BinaryPrimitives.ReadUInt32LittleEndian(m);
            uint sizeWord = BinaryPrimitives.ReadUInt32LittleEndian(m[4..]);
            uint offsetBlock = BinaryPrimitives.ReadUInt32LittleEndian(m[12..]);
            entries.TryAdd(hash, new Entry(
                (ulong)offsetBlock * 16,
                type == ChunkDirectory,
                (compressedWord & 0x1000_0000) != 0,
                sizeWord & 0x0FFF_FFFF,
                compressedWord & 0x0FFF_FFFF));
        }
        return entries;
    }

    public ulong HashPath(string path)
    {
        path = PackageReader.Normalize(path);
        return CityHash.CityHash64(_salt != 0 ? _salt + path : path);
    }

    public bool Contains(string path) => _entries.ContainsKey(HashPath(path));

    public bool TryReadFile(string path, out byte[] data)
    {
        if (_entries.TryGetValue(HashPath(path), out var entry) && !entry.IsDirectory)
            return TryRead(entry, out data);
        data = [];
        return false;
    }

    /// <summary>Names in a directory; subdirectories are prefixed with '*' (v1 convention) for both versions.</summary>
    public IReadOnlyList<string> ListDirectory(string path)
    {
        if (!_entries.TryGetValue(HashPath(path), out var entry) || !entry.IsDirectory || !TryRead(entry, out var data))
            return [];
        if (Version == 1)
            return Encoding.UTF8.GetString(data).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var names = new List<string>();
        if (data.Length < 4) return names;
        int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(data);
        int pos = 4 + count;
        for (int i = 0; i < count && pos <= data.Length; i++)
        {
            int len = data[4 + i];
            if (pos + len > data.Length) break;
            var name = Encoding.UTF8.GetString(data, pos, len);
            names.Add(name.StartsWith('/') ? "*" + name[1..] : name);
            pos += len;
        }
        return names;
    }

    bool TryRead(Entry entry, out byte[] data)
    {
        data = [];
        if (entry.Size > PackageReader.MaxEntrySize || entry.CompressedSize > PackageReader.MaxEntrySize) return false;
        try
        {
            byte[] raw;
            lock (_stream) raw = ReadAt(_stream, (long)entry.Offset, (int)entry.CompressedSize);
            if (!entry.IsCompressed)
            {
                data = raw;
                return true;
            }
            using var z = new ZLibStream(new MemoryStream(raw), CompressionMode.Decompress);
            data = new byte[entry.Size];
            z.ReadExactly(data);
            return true;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentOutOfRangeException)
        {
            data = [];
            return false;
        }
    }

    static byte[] ReadAt(FileStream fs, long offset, int length)
    {
        var buf = new byte[length];
        fs.Position = offset;
        fs.ReadExactly(buf);
        return buf;
    }

    static byte[] Inflate(byte[] data)
    {
        using var z = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
        using var output = new MemoryStream();
        z.CopyTo(output);
        return output.ToArray();
    }

    public void Dispose() => _stream.Dispose();
}
