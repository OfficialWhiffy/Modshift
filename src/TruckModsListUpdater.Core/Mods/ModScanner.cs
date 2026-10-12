using TruckModsListUpdater.Core.Archives;
using TruckModsListUpdater.Core.Games;
using TruckModsListUpdater.Core.Sii;

namespace TruckModsListUpdater.Core.Mods;

/// <summary>All mod packages available to a game, keyed by package id.</summary>
public sealed class ModCatalog(IReadOnlyList<ModPackage> packages)
{
    readonly Dictionary<string, ModPackage> _byId = packages
        .GroupBy(p => p.PackageId, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ModPackage> Packages { get; } = packages;

    public ModPackage? Find(string packageId) => _byId.GetValueOrDefault(packageId);
}

/// <summary>
/// Finds mod packages in the game's mod folder and in every Steam library's workshop folder and
/// reads their manifests (through <see cref="ModMetadataCache"/>).
/// </summary>
public sealed class ModScanner(ModMetadataCache cache)
{
    public async Task<ModCatalog> ScanAsync(GameInstallation install, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var candidates = new List<Candidate>();
        candidates.AddRange(LocalCandidates(install.ModDir));
        candidates.AddRange(WorkshopCandidates(install));

        // Two at a time: gentle on spinning disks, still fast since only indexes are read.
        var packages = new ModPackage[candidates.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, candidates.Count),
            new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = ct },
            (i, _) =>
            {
                progress?.Report(Path.GetFileName(candidates[i].Path));
                packages[i] = Read(candidates[i]);
                return ValueTask.CompletedTask;
            });

        return new ModCatalog(FlagDuplicates(packages)
            .OrderBy(p => p.Source)
            .ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList());
    }

    sealed record Candidate(string PackageId, ModSource Source, string Path, ulong? WorkshopId, ModIssue? Issue = null);

    static IEnumerable<Candidate> LocalCandidates(string modDir)
    {
        if (!Directory.Exists(modDir)) yield break;
        foreach (var file in Directory.EnumerateFiles(modDir))
        {
            var ext = Path.GetExtension(file);
            if (ext.Equals(".scs", StringComparison.OrdinalIgnoreCase) || ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                yield return new Candidate(Path.GetFileNameWithoutExtension(file), ModSource.Local, file, null);
        }
        foreach (var dir in Directory.EnumerateDirectories(modDir))
        {
            var issue = File.Exists(Path.Combine(dir, "manifest.sii"))
                ? null
                : new ModIssue(ModIssueKind.BrokenFolder, IssueSeverity.Warning,
                    "This folder has no manifest.sii. The game still loads it as a broken mod; archive it unless it is a mod you unpacked yourself.");
            yield return new Candidate(Path.GetFileName(dir), ModSource.Local, dir, null, issue);
        }
    }

    static IEnumerable<Candidate> WorkshopCandidates(GameInstallation install)
    {
        foreach (var root in install.WorkshopDirs)
        {
            foreach (var itemDir in Directory.EnumerateDirectories(root))
            {
                if (!ulong.TryParse(Path.GetFileName(itemDir), out var id)) continue;
                var package = WorkshopItem.ResolvePackage(itemDir, install.GameVersion);
                var packageId = ActiveModEntry.WorkshopPackageId(id);
                yield return package == null
                    ? new Candidate(packageId, ModSource.Workshop, itemDir, id,
                        new ModIssue(ModIssueKind.Unreadable, IssueSeverity.Error, "No package for this game version was found in the workshop item (still downloading?)."))
                    : new Candidate(packageId, ModSource.Workshop, package, id);
            }
        }
    }

    ModPackage Read(Candidate c)
    {
        var isDir = Directory.Exists(c.Path);
        var (size, mtime) = isDir ? FolderStamp(c.Path) : FileStamp(c.Path);
        var kind = c.Issue?.Kind == ModIssueKind.Unreadable ? PackageKind.Unknown : PackageReader.DetectKind(c.Path);
        var package = new ModPackage
        {
            PackageId = c.PackageId,
            Source = c.Source,
            Kind = kind,
            Path = c.Path,
            Size = size,
            LastWriteUtc = mtime,
            WorkshopId = c.WorkshopId,
        };
        var issues = new List<ModIssue>();
        if (c.Issue != null) issues.Add(c.Issue);
        if (c.Issue != null) return package with { Issues = issues };

        var key = ModMetadataCache.KeyFor(c.Path, size, mtime);
        if (!cache.TryGet(key, out var manifest, out var description, out var icon, out var error))
        {
            try
            {
                using var reader = PackageReader.Open(c.Path);
                var metadata = ManifestReader.Read(reader);
                (manifest, description) = (metadata.Manifest, metadata.Description);
                icon = cache.Put(key, metadata, null);
            }
            catch (Exception e) when (e is PackageFormatException or SiiFormatException or IOException or UnauthorizedAccessException)
            {
                error = e.Message;
                cache.Put(key, null, error);
            }
        }

        if (error != null) issues.Add(new ModIssue(ModIssueKind.Unreadable, IssueSeverity.Error, $"Could not read this package: {error}"));
        else if (manifest == null) issues.Add(new ModIssue(ModIssueKind.NoManifest, IssueSeverity.Info, "No manifest.sii; the file name is used as its name."));

        return package with { Manifest = manifest, Description = description, IconPath = icon, Issues = issues };
    }

    static (long, DateTime) FileStamp(string path)
    {
        var fi = new FileInfo(path);
        return (fi.Length, fi.LastWriteTimeUtc);
    }

    /// <summary>Folder mods can be huge; stamp them by their manifest instead of walking every file.</summary>
    static (long, DateTime) FolderStamp(string dir)
    {
        var manifest = new FileInfo(Path.Combine(dir, "manifest.sii"));
        return manifest.Exists ? (0, manifest.LastWriteTimeUtc) : (0, Directory.GetLastWriteTimeUtc(dir));
    }

    /// <summary>The game can load only one package per id (e.g. "x.scs" and "x.zip").</summary>
    static IEnumerable<ModPackage> FlagDuplicates(IEnumerable<ModPackage> packages)
    {
        foreach (var group in packages.GroupBy(p => p.PackageId, StringComparer.OrdinalIgnoreCase))
        {
            var list = group.ToList();
            if (list.Count == 1) { yield return list[0]; continue; }
            var names = string.Join(", ", list.Select(p => Path.GetFileName(p.Path)));
            foreach (var p in list)
            {
                yield return p with
                {
                    Issues = [.. p.Issues, new ModIssue(ModIssueKind.DuplicateId, IssueSeverity.Warning,
                        $"Several packages share the id '{p.PackageId}' ({names}); the game loads only one of them.")],
                };
            }
        }
    }
}
