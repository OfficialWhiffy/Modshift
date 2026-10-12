using System.Text.Json;
using TruckModsListUpdater.Core.Sii;

namespace TruckModsListUpdater.Core.Mods;

public sealed record ArchivedMod(string ArchivedPath, string OriginalPath, string Reason, DateTime ArchivedLocal)
{
    public string Name => Path.GetFileName(OriginalPath);
}

public sealed class ModArchiveException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Moves mods out of the game's mod folder (the game loads everything in it, including
/// sub-folders) into an archive folder it does not scan, and back again. Nothing is deleted.
/// </summary>
public sealed class ModArchive(string archiveDir)
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    string IndexPath => Path.Combine(archiveDir, "archive.json");

    public string ArchiveDir => archiveDir;

    public IReadOnlyList<ArchivedMod> List() =>
        LoadIndex().Where(i => File.Exists(i.ArchivedPath) || Directory.Exists(i.ArchivedPath))
            .OrderByDescending(i => i.ArchivedLocal).ToList();

    /// <summary>Moves a mod file or folder into the archive. Returns the new entry.</summary>
    public ArchivedMod Archive(string path, string reason)
    {
        bool isDir = Directory.Exists(path);
        if (!isDir && !File.Exists(path)) throw new ModArchiveException($"{Path.GetFileName(path)} no longer exists.");

        var dayDir = Path.Combine(archiveDir, DateTime.Now.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(dayDir);
        var target = UniquePath(Path.Combine(dayDir, Path.GetFileName(path)));
        try
        {
            if (isDir) Directory.Move(path, target);
            else File.Move(path, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ModArchiveException($"Could not move {Path.GetFileName(path)} to the archive: {e.Message}", e);
        }

        var item = new ArchivedMod(target, Path.GetFullPath(path), reason, DateTime.Now);
        var index = LoadIndex();
        index.Add(item);
        SaveIndex(index);
        return item;
    }

    /// <summary>Moves an archived mod back to where it came from; refuses to overwrite.</summary>
    public void Restore(ArchivedMod item)
    {
        if (File.Exists(item.OriginalPath) || Directory.Exists(item.OriginalPath))
            throw new ModArchiveException($"{item.Name} already exists in the mod folder. Archive or remove that one first.");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(item.OriginalPath)!);
            if (Directory.Exists(item.ArchivedPath)) Directory.Move(item.ArchivedPath, item.OriginalPath);
            else File.Move(item.ArchivedPath, item.OriginalPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ModArchiveException($"Could not restore {item.Name}: {e.Message}", e);
        }
        SaveIndex(LoadIndex().Where(i => i.ArchivedPath != item.ArchivedPath).ToList());
    }

    /// <summary>
    /// Suggestions for cleaning up the mod folder: folders without a manifest, older versions of
    /// installed mods, and (optionally) local mods that no profile uses.
    /// </summary>
    public static IReadOnlyList<CleanupSuggestion> SuggestCleanup(ModCatalog catalog, IEnumerable<IReadOnlyList<ActiveModEntry>> profileMods)
    {
        var used = new HashSet<string>(profileMods.SelectMany(m => m).Select(e => e.PackageId), StringComparer.OrdinalIgnoreCase);
        var local = catalog.Packages.Where(p => !p.IsWorkshop).ToList();
        var suggestions = new List<CleanupSuggestion>();

        foreach (var p in local.Where(p => p.Issues.Any(i => i.Kind == ModIssueKind.BrokenFolder)))
            suggestions.Add(new CleanupSuggestion(p, CleanupReason.BrokenFolder, "Folder without manifest.sii; the game loads it as a broken mod.", true));

        foreach (var p in local)
        {
            if (suggestions.Any(s => s.Package == p)) continue;
            var newer = local.Where(o => ModIdentity.IsSameMod(p, o) && ModIdentity.Compare(o, p) == VersionComparison.Newer).ToList();
            if (newer.Count > 0)
            {
                suggestions.Add(new CleanupSuggestion(p, CleanupReason.OlderVersion,
                    $"Older version of {Path.GetFileName(newer[0].Path)}.", !used.Contains(p.PackageId)));
            }
        }

        foreach (var p in local.Where(p => !used.Contains(p.PackageId) && suggestions.All(s => s.Package != p)))
            suggestions.Add(new CleanupSuggestion(p, CleanupReason.Unused, "Not enabled in any profile.", false));

        return suggestions;
    }

    List<ArchivedMod> LoadIndex()
    {
        try
        {
            return File.Exists(IndexPath) ? JsonSerializer.Deserialize<List<ArchivedMod>>(File.ReadAllText(IndexPath)) ?? [] : [];
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return [];
        }
    }

    void SaveIndex(List<ArchivedMod> index)
    {
        Directory.CreateDirectory(archiveDir);
        File.WriteAllText(IndexPath + ".tmp", JsonSerializer.Serialize(index, Json));
        File.Move(IndexPath + ".tmp", IndexPath, overwrite: true);
    }

    internal static string UniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }
}

public enum CleanupReason { BrokenFolder, OlderVersion, Unused }

/// <summary>A mod the user may want to archive; <see cref="Recommended"/> ones are pre-selected.</summary>
public sealed record CleanupSuggestion(ModPackage Package, CleanupReason Reason, string Explanation, bool Recommended);
