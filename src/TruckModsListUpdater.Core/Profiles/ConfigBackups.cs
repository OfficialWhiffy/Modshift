using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using TruckModsListUpdater.Core.Games;

namespace TruckModsListUpdater.Core.Profiles;

/// <summary>
/// One file in a settings backup. <see cref="Entry"/> is the zip entry with the file as it was before
/// the change, or null when the file did not exist. <see cref="AfterSha256"/> is the file as the change
/// left it, or null when the change removed it, so a restore can tell whether something else changed it since.
/// </summary>
public sealed record ConfigBackupFile(ConfigRoot Root, string FileName, string? Entry, string? AfterSha256);

public sealed record ConfigBackupManifest(int Schema, string GameId, string ProfileFolder, string Description, IReadOnlyList<ConfigBackupFile> Files)
{
    public const int CurrentSchema = 1;
}

public sealed record ConfigBackup(string Path, DateTime CreatedLocal, string Description)
{
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
}

/// <summary>
/// Zip backups of the settings files a copy or restore changes, in
/// %LOCALAPPDATA%\Modshift\config-backups\{game}\{local|cloud}-{folder}. Each zip holds manifest.json
/// and the files as they were before the change. Nothing is written next to the profile.
/// </summary>
public sealed class ConfigBackups(string backupsRoot, int keepLatest = 30)
{
    const string TimestampFormat = "yyyyMMdd_HHmmss_fff";
    const string ManifestEntry = "manifest.json";

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string DirFor(GameDefinition game, ProfileRef profile) =>
        Path.Combine(backupsRoot, game.Id, $"{(profile.IsCloud ? "cloud" : "local")}-{profile.FolderName}");

    /// <summary>A file to include: its location, its current contents (null = does not exist) and the hash it will have afterwards.</summary>
    public sealed record Item(ConfigRoot Root, string FileName, byte[]? Before, string? AfterSha256);

    public ConfigBackup Create(GameDefinition game, ProfileRef profile, string description, IReadOnlyList<Item> items, DateTime? now = null)
    {
        var dir = DirFor(game, profile);
        Directory.CreateDirectory(dir);
        var time = now ?? DateTime.Now;
        var path = Path.Combine(dir, time.ToString(TimestampFormat, CultureInfo.InvariantCulture) + ".zip");
        // Two backups in the same millisecond (a fast restore after a copy) must not overwrite each other.
        for (int n = 1; File.Exists(path); n++)
            path = Path.Combine(dir, time.ToString(TimestampFormat, CultureInfo.InvariantCulture) + $"_{n}.zip");

        var files = new List<ConfigBackupFile>();
        using (var zip = ZipFile.Open(path + ".tmp", ZipArchiveMode.Create))
        {
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                string? entry = null;
                if (item.Before != null)
                {
                    entry = $"files/{i}/{item.FileName}";
                    using var s = zip.CreateEntry(entry, CompressionLevel.Optimal).Open();
                    s.Write(item.Before);
                }
                files.Add(new ConfigBackupFile(item.Root, item.FileName, entry, item.AfterSha256));
            }

            var manifest = new ConfigBackupManifest(ConfigBackupManifest.CurrentSchema, game.Id, profile.FolderName, description, files);
            using var m = zip.CreateEntry(ManifestEntry, CompressionLevel.Optimal).Open();
            JsonSerializer.Serialize(m, manifest, Json);
        }
        File.Move(path + ".tmp", path);
        Prune(dir);
        return new ConfigBackup(path, time, description);
    }

    public IReadOnlyList<ConfigBackup> List(GameDefinition game, ProfileRef profile)
    {
        var dir = DirFor(game, profile);
        if (!Directory.Exists(dir)) return [];
        var list = new List<ConfigBackup>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.zip"))
        {
            if (!TryParseTime(path, out var time)) continue;
            string description;
            try { description = ReadManifest(path).Description; }
            catch (Exception e) when (e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException) { continue; }
            list.Add(new ConfigBackup(path, time, description));
        }
        return list.OrderByDescending(b => b.CreatedLocal).ThenByDescending(b => b.Path, StringComparer.Ordinal).ToList();
    }

    public static ConfigBackupManifest ReadManifest(string backupPath)
    {
        using var zip = ZipFile.OpenRead(backupPath);
        var entry = zip.GetEntry(ManifestEntry) ?? throw new InvalidDataException("The backup has no manifest.json.");
        using var s = entry.Open();
        var manifest = JsonSerializer.Deserialize<ConfigBackupManifest>(s, Json) ?? throw new InvalidDataException("The backup's manifest is empty.");
        if (manifest.Schema != ConfigBackupManifest.CurrentSchema)
            throw new InvalidDataException($"The backup was made by a newer version (format {manifest.Schema}).");
        return manifest;
    }

    public static byte[] ReadEntry(string backupPath, string entryName)
    {
        using var zip = ZipFile.OpenRead(backupPath);
        var entry = zip.GetEntry(entryName) ?? throw new InvalidDataException($"The backup does not contain {entryName}.");
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    static bool TryParseTime(string path, out DateTime time)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (name.Length > TimestampFormat.Length && name[TimestampFormat.Length] == '_') name = name[..TimestampFormat.Length];
        return DateTime.TryParseExact(name, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }

    void Prune(string dir)
    {
        var old = Directory.EnumerateFiles(dir, "*.zip")
            .Where(p => TryParseTime(p, out _))
            .OrderByDescending(p => Path.GetFileName(p), StringComparer.Ordinal)
            .Skip(keepLatest);
        foreach (var p in old)
        {
            try { File.Delete(p); } catch (IOException) { }
        }
    }
}
