using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TruckModsListUpdater.Core.Mods;

/// <summary>
/// Remembers manifests of packages that have not changed (same path, size and modification time),
/// so a rescan of many gigabytes of mods only touches new or updated files.
/// </summary>
public sealed class ModMetadataCache(string cacheDir)
{
    // 2: descriptions are cached with their colour tags instead of stripped.
    const int SchemaVersion = 2;

    sealed record Entry(string Key, ModManifest? Manifest, string? Description, string? IconFile, string? Error);
    sealed record CacheFile(int Schema, List<Entry> Entries);

    readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string, bool> _used = new(StringComparer.OrdinalIgnoreCase);
    string CacheFilePath => Path.Combine(cacheDir, "mods.json");
    public string IconDir => Path.Combine(cacheDir, "icons");

    public static string KeyFor(string path, long size, DateTime lastWriteUtc) =>
        $"{Path.GetFullPath(path)}|{size}|{lastWriteUtc.Ticks}";

    public void Load()
    {
        try
        {
            if (!File.Exists(CacheFilePath)) return;
            var file = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(CacheFilePath));
            if (file?.Schema != SchemaVersion) return;
            foreach (var e in file.Entries) _entries[e.Key] = e;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt cache only costs a full rescan.
        }
    }

    /// <summary>Saves entries used since <see cref="Load"/> (dropping packages that no longer exist).</summary>
    public void Save()
    {
        Directory.CreateDirectory(cacheDir);
        var used = _entries.Values.Where(e => _used.ContainsKey(e.Key)).OrderBy(e => e.Key).ToList();
        var tmp = CacheFilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(new CacheFile(SchemaVersion, used)));
        File.Move(tmp, CacheFilePath, overwrite: true);
    }

    public bool TryGet(string key, out ModManifest? manifest, out string? description, out string? iconPath, out string? error)
    {
        manifest = null; description = null; iconPath = null; error = null;
        if (!_entries.TryGetValue(key, out var e)) return false;
        if (e.IconFile != null && !File.Exists(Path.Combine(IconDir, e.IconFile))) return false;
        _used[key] = true;
        (manifest, description, error) = (e.Manifest, e.Description, e.Error);
        iconPath = e.IconFile == null ? null : Path.Combine(IconDir, e.IconFile);
        return true;
    }

    /// <summary>Stores metadata; returns the cached icon path.</summary>
    public string? Put(string key, PackageMetadata? metadata, string? error)
    {
        string? iconFile = null;
        if (metadata?.Icon is { } icon)
        {
            iconFile = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant() + metadata.IconExtension;
            Directory.CreateDirectory(IconDir);
            File.WriteAllBytes(Path.Combine(IconDir, iconFile), icon);
        }
        _entries[key] = new Entry(key, metadata?.Manifest, metadata?.Description, iconFile, error);
        _used[key] = true;
        return iconFile == null ? null : Path.Combine(IconDir, iconFile);
    }
}
