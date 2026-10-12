using System.IO.Compression;
using System.Text;
using TruckModsListUpdater.Core.Archives;

namespace TruckModsListUpdater.Core.Tests.Fixtures;

/// <summary>Builds small mod packages (ZIP, protected ZIP, HashFS v1) for tests.</summary>
public static class PackageBuilder
{
    public static string Manifest(string displayName, string version = "1.0", string? extra = null) => $$"""
        SiiNunit
        {
        mod_package : .package_name {
            package_version: "{{version}}"
            display_name: "{{displayName}}"
            author: "Tester"
            category[]: "truck"
            icon: "icon.jpg"
            description_file: "description.txt"
            {{extra}}
        }
        }
        """;

    public static void Zip(string path, IReadOnlyDictionary<string, byte[]> files)
    {
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (name, data) in files)
        {
            using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
            s.Write(data);
        }
    }

    /// <summary>
    /// A ZIP "protected" like many mods: local headers claim compression method 1, the general
    /// purpose flag claims encryption and all CRCs are wrong. The game still loads these.
    /// </summary>
    public static void ProtectedZip(string path, IReadOnlyDictionary<string, byte[]> files)
    {
        Zip(path, files);
        var bytes = File.ReadAllBytes(path);
        for (int i = 0; i + 4 <= bytes.Length; i++)
        {
            uint sig = BitConverter.ToUInt32(bytes, i);
            if (sig == 0x04034b50) // local header
            {
                bytes[i + 6] = 1; // flags: encrypted
                bytes[i + 8] = 1; bytes[i + 9] = 0; // method: shrink
                bytes[i + 14] ^= 0x5A; // crc
            }
            else if (sig == 0x02014b50) // central header
            {
                bytes[i + 8] = 1; // flags: encrypted
                bytes[i + 16] ^= 0xA5; // crc
            }
        }
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>Writes a HashFS v1 archive with zlib-compressed files and directory listings.</summary>
    public static void HashFs(string path, IReadOnlyDictionary<string, byte[]> files)
    {
        var entries = new List<(ulong Hash, byte[] Data, uint Flags, uint Size)>();

        // Directory listings: files by name, subdirectories prefixed with '*'.
        var dirs = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal) { [""] = [] };
        foreach (var file in files.Keys)
        {
            var parts = file.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                var parent = string.Join('/', parts[..i]);
                if (!dirs.TryGetValue(parent, out var set)) dirs[parent] = set = [];
                set.Add(i == parts.Length - 1 ? parts[i] : "*" + parts[i]);
            }
        }
        foreach (var (dir, names) in dirs)
            entries.Add((CityHash.CityHash64(dir), Encoding.UTF8.GetBytes(string.Join('\n', names)), 1u, 0));
        foreach (var (name, data) in files)
            entries.Add((CityHash.CityHash64(name), Compress(data), 2u, (uint)data.Length));

        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        const ulong tableOffset = 4096;
        w.Write("SCS#"u8); w.Write((ushort)1); w.Write((ushort)0); w.Write("CITY"u8);
        w.Write((uint)entries.Count); w.Write(tableOffset);

        ulong dataOffset = tableOffset + (ulong)entries.Count * 32;
        fs.Position = (long)tableOffset;
        foreach (var e in entries)
        {
            uint size = e.Flags == 1 ? (uint)e.Data.Length : e.Size;
            w.Write(e.Hash); w.Write(dataOffset); w.Write(e.Flags); w.Write(0u); w.Write(size); w.Write((uint)e.Data.Length);
            dataOffset += (ulong)e.Data.Length;
        }
        foreach (var e in entries) w.Write(e.Data);
    }

    /// <summary>Writes a HashFS v2 archive (compressed entry and metadata tables, binary directory listings).</summary>
    public static void HashFsV2(string path, IReadOnlyDictionary<string, byte[]> files)
    {
        var dirs = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal) { [""] = [] };
        foreach (var file in files.Keys)
        {
            var parts = file.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                var parent = string.Join('/', parts[..i]);
                if (!dirs.TryGetValue(parent, out var set)) dirs[parent] = set = [];
                set.Add(i == parts.Length - 1 ? parts[i] : "/" + parts[i]);
            }
        }

        var items = new List<(ulong Hash, byte[] Stored, uint Size, bool Dir)>();
        foreach (var (dir, names) in dirs)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write((uint)names.Count);
            foreach (var n in names) w.Write((byte)Encoding.UTF8.GetByteCount(n));
            foreach (var n in names) w.Write(Encoding.UTF8.GetBytes(n));
            var listing = ms.ToArray();
            items.Add((CityHash.CityHash64(dir), Compress(listing), (uint)listing.Length, true));
        }
        foreach (var (name, data) in files)
            items.Add((CityHash.CityHash64(name), Compress(data), (uint)data.Length, false));

        // File data starts after a 4 KiB header area, each item aligned to 16 bytes.
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        long dataPos = 4096;
        var entryTable = new MemoryStream();
        var metaTable = new MemoryStream();
        using (var et = new BinaryWriter(entryTable, Encoding.UTF8, leaveOpen: true))
        using (var mt = new BinaryWriter(metaTable, Encoding.UTF8, leaveOpen: true))
        {
            foreach (var item in items)
            {
                fs.Position = dataPos;
                bw.Write(item.Stored);
                uint metaIndex = (uint)(metaTable.Length / 4);
                et.Write(item.Hash); et.Write(metaIndex); et.Write((ushort)1); et.Write((ushort)0);
                mt.Write((uint)((item.Dir ? 129u : 128u) << 24)); // chunk header: index 0, type
                mt.Write((uint)item.Stored.Length | 0x1000_0000u); // compressed size + zlib flag
                mt.Write(item.Size);
                mt.Write(0u);
                mt.Write((uint)(dataPos / 16));
                dataPos = (fs.Position + 15) / 16 * 16;
            }
        }
        var et2 = Compress(entryTable.ToArray());
        var mt2 = Compress(metaTable.ToArray());
        long etStart = dataPos, mtStart = dataPos + et2.Length;
        fs.Position = etStart; bw.Write(et2); bw.Write(mt2);

        fs.Position = 0;
        bw.Write("SCS#"u8); bw.Write((ushort)2); bw.Write((ushort)0); bw.Write("CITY"u8);
        bw.Write((uint)items.Count); bw.Write((uint)et2.Length); bw.Write((uint)(metaTable.Length / 4)); bw.Write((uint)mt2.Length);
        bw.Write((ulong)etStart); bw.Write((ulong)mtStart);
    }

    static byte[] Compress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(data);
        return ms.ToArray();
    }

    public static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);
}
