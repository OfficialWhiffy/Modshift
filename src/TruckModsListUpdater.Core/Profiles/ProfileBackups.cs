using System.Globalization;
using System.IO.Compression;
using TruckModsListUpdater.Core.Games;

namespace TruckModsListUpdater.Core.Profiles;

public sealed record ProfileBackup(string Path, DateTime CreatedLocal)
{
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
}

/// <summary>
/// Zip backups of a profile's top-level files (profile.sii, profile.bak.sii, config...), kept in the
/// app's own folder so nothing extra ends up in the game's profile folder. Saves are not included:
/// the manager never touches them.
/// </summary>
public sealed class ProfileBackups(string backupsRoot, int keepLatest = 20, int keepDailyDays = 30)
{
    const string TimestampFormat = "yyyyMMdd_HHmmss_fff";

    public string DirFor(GameDefinition game, ProfileRef profile) =>
        Path.Combine(backupsRoot, game.Id, $"{(profile.IsCloud ? "cloud" : "local")}-{profile.FolderName}");

    public ProfileBackup Create(GameDefinition game, ProfileRef profile, DateTime? now = null)
    {
        var dir = DirFor(game, profile);
        Directory.CreateDirectory(dir);
        var time = now ?? DateTime.Now;
        var path = Path.Combine(dir, time.ToString(TimestampFormat, CultureInfo.InvariantCulture) + ".zip");

        using (var zip = ZipFile.Open(path + ".tmp", ZipArchiveMode.Create))
        {
            foreach (var file in Directory.EnumerateFiles(profile.Dir))
            {
                if (file.EndsWith(".tmm.tmp", StringComparison.OrdinalIgnoreCase)) continue;
                using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var target = zip.CreateEntry(Path.GetFileName(file), CompressionLevel.Optimal).Open();
                source.CopyTo(target);
            }
        }
        File.Move(path + ".tmp", path, overwrite: true);
        Prune(dir, time);
        return new ProfileBackup(path, time);
    }

    public IReadOnlyList<ProfileBackup> List(GameDefinition game, ProfileRef profile)
    {
        var dir = DirFor(game, profile);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*.zip")
            .Select(p => DateTime.TryParseExact(Path.GetFileNameWithoutExtension(p), TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var t) ? new ProfileBackup(p, t) : null)
            .OfType<ProfileBackup>()
            .OrderByDescending(b => b.CreatedLocal)
            .ToList();
    }

    /// <summary>Reads profile.sii out of a backup.</summary>
    public static byte[] ReadProfileSii(ProfileBackup backup)
    {
        using var zip = ZipFile.OpenRead(backup.Path);
        var entry = zip.GetEntry("profile.sii") ?? throw new InvalidDataException("The backup does not contain profile.sii.");
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Keeps the newest <c>keepLatest</c> backups plus the newest one of each day for <c>keepDailyDays</c> days.</summary>
    void Prune(string dir, DateTime now)
    {
        var backups = Directory.EnumerateFiles(dir, "*.zip")
            .Select(p => (Path: p, Ok: DateTime.TryParseExact(Path.GetFileNameWithoutExtension(p), TimestampFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var t), Time: t))
            .Where(b => b.Ok)
            .OrderByDescending(b => b.Time)
            .ToList();

        var keep = new HashSet<string>(backups.Take(keepLatest).Select(b => b.Path));
        foreach (var day in backups.Where(b => b.Time >= now.Date.AddDays(-keepDailyDays)).GroupBy(b => b.Time.Date))
            keep.Add(day.First().Path);

        foreach (var b in backups.Where(b => !keep.Contains(b.Path)))
        {
            try { File.Delete(b.Path); } catch (IOException) { }
        }
    }
}
