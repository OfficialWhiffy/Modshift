using System.IO.Compression;
using System.Security.Cryptography;
using TruckModsListUpdater.Core.Archives;
using TruckModsListUpdater.Core.Games;
using TruckModsListUpdater.Core.Sii;

namespace TruckModsListUpdater.Core.Mods;

public enum InstallConflict { None, AlreadyInstalled, SameFileName, NewerVersionOf, OlderVersionOf, OtherVersionOf }

public enum InstallAction { Install, Replace, Skip }

/// <summary>One mod found in what the user dropped, staged next to the mod folder and ready to install.</summary>
public sealed class InstallCandidate
{
    public required ModPackage Package { get; init; }
    /// <summary>Where it came from ("file.zip › mod.scs").</summary>
    public required string SourceDescription { get; init; }
    public required string TargetName { get; init; }
    public InstallConflict Conflict { get; init; }
    public ModPackage? Existing { get; init; }
    public InstallAction Action { get; set; }

    public string ConflictText => Conflict switch
    {
        InstallConflict.AlreadyInstalled => "Already installed (identical file).",
        InstallConflict.SameFileName => $"Replaces the installed {TargetName}.",
        InstallConflict.NewerVersionOf => $"Newer version of {Path.GetFileName(Existing!.Path)}.",
        InstallConflict.OlderVersionOf => $"Older than the installed {Path.GetFileName(Existing!.Path)}.",
        InstallConflict.OtherVersionOf => $"Another version of {Path.GetFileName(Existing!.Path)}.",
        _ => "New mod.",
    };
}

public sealed record InstallPlan(string StagingDir, IReadOnlyList<InstallCandidate> Candidates, IReadOnlyList<string> Problems);

public sealed record InstalledMod(InstallCandidate Candidate, string InstalledPath, ArchivedMod? Replaced);

/// <summary>
/// Installs mods from files, folders and download archives into the game's mod folder.
/// Everything is first extracted/copied to a staging folder next to the mod folder (same drive),
/// then moved in, so a half-copied file never ends up where the game loads it.
/// </summary>
public sealed class ModInstaller(GameInstallation install, ModArchive archive)
{
    static readonly string[] ModRootMarkers =
        ["manifest.sii", "def/", "map/", "material/", "model/", "model2/", "prefab/", "prefab2/", "vehicle/", "sound/", "ui/", "font/", "unit/", "automat/", "effect/", "system/"];

    public string StagingRoot => Path.Combine(install.DocumentsDir, ".tmm_staging");

    public async Task<InstallPlan> AnalyzeAsync(IEnumerable<string> droppedPaths, ModCatalog catalog, CancellationToken ct = default)
    {
        var staging = Path.Combine(StagingRoot, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(staging);
        var staged = new List<(string Path, string Source)>();
        var problems = new List<string>();

        foreach (var path in droppedPaths)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await Task.Run(() => Stage(path, staging, staged, problems, Path.GetFileName(path), ct), ct);
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or PackageFormatException)
            {
                problems.Add($"{Path.GetFileName(path)}: {e.Message}");
            }
        }

        var cache = new ModMetadataCache(Path.Combine(staging, ".meta"));
        var candidates = new List<InstallCandidate>();
        foreach (var (stagedPath, source) in staged)
        {
            var package = ReadStaged(stagedPath, cache);
            if (candidates.Any(c => c.TargetName.Equals(Path.GetFileName(stagedPath), StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add($"{source}: skipped, another dropped mod has the same file name.");
                continue;
            }
            candidates.Add(Classify(package, source, catalog));
        }
        return new InstallPlan(staging, candidates, problems);
    }

    void Stage(string path, string staging, List<(string, string)> staged, List<string> problems, string source, CancellationToken ct)
    {
        if (Directory.Exists(path))
        {
            if (File.Exists(Path.Combine(path, "manifest.sii")))
            {
                var target = Path.Combine(Slot(staging), Path.GetFileName(path));
                CopyDirectory(path, target, ct);
                staged.Add((target, source));
                return;
            }
            // A folder of downloads: look for mod archives inside.
            var files = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories).Where(IsArchiveName).ToList();
            if (files.Count == 0) problems.Add($"{source}: no mods found in this folder.");
            foreach (var f in files) Stage(f, staging, staged, problems, $"{source} › {Path.GetFileName(f)}", ct);
            return;
        }

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".7z" or ".rar")
        {
            problems.Add($"{source}: {ext} archives are not supported yet. Extract it first and drop the .scs files.");
            return;
        }
        if (ext is not (".scs" or ".zip"))
        {
            problems.Add($"{source}: not a mod file (.scs or .zip).");
            return;
        }

        var kind = PackageReader.DetectKind(path);
        if (kind == PackageKind.HashFs || (kind == PackageKind.Zip && LooksLikeMod(path)))
        {
            var target = Path.Combine(Slot(staging), Path.GetFileName(path));
            CopyFile(path, target, ct);
            staged.Add((target, source));
        }
        else if (kind == PackageKind.Zip)
        {
            ExtractContainer(path, staging, staged, problems, source, ct);
        }
        else
        {
            problems.Add($"{source}: not a mod (unknown file format).");
        }
    }

    /// <summary>A mod ZIP has manifest.sii or game folders (def/, map/, ...) at its root.</summary>
    static bool LooksLikeMod(string zipPath)
    {
        using var reader = new ZipPackageReader(zipPath);
        return reader.EntryNames.Any(n => ModRootMarkers.Any(m => m.EndsWith('/') ? n.StartsWith(m, StringComparison.OrdinalIgnoreCase) : n.Equals(m, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Extracts the mods inside a downloaded ZIP (the .scs files and mod ZIPs, or a mod folder with manifest.sii).</summary>
    static void ExtractContainer(string zipPath, string staging, List<(string, string)> staged, List<string> problems, string source, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        int found = 0;
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith('/') || !IsArchiveName(entry.Name)) continue;
            EnsureFreeSpace(staging, entry.Length);
            var target = Path.Combine(Slot(staging), entry.Name);
            using (var input = entry.Open())
            using (var output = File.Create(target))
                input.CopyTo(output);
            found++;
            var inner = $"{source} › {entry.Name}";
            if (PackageReader.DetectKind(target) == PackageKind.Zip && !LooksLikeMod(target))
            {
                // A zip inside the zip that is itself a container.
                ExtractContainer(target, staging, staged, problems, inner, ct);
                File.Delete(target);
            }
            else staged.Add((target, inner));
        }

        // A zipped mod folder: "<name>/manifest.sii".
        foreach (var manifest in zip.Entries.Where(e => e.Name.Equals("manifest.sii", StringComparison.OrdinalIgnoreCase) && e.FullName.Count(c => c == '/') == 1))
        {
            var folder = manifest.FullName[..manifest.FullName.IndexOf('/')];
            var target = Path.Combine(Slot(staging), folder);
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(folder + "/", StringComparison.Ordinal) && !e.FullName.EndsWith('/')))
            {
                var dest = Path.GetFullPath(Path.Combine(target, entry.FullName[(folder.Length + 1)..]));
                if (!dest.StartsWith(target, StringComparison.OrdinalIgnoreCase)) continue; // zip-slip
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest);
            }
            staged.Add((target, $"{source} › {folder}"));
            found++;
        }

        if (found == 0) problems.Add($"{source}: no mods found inside this archive.");
    }

    /// <summary>Each staged item gets its own folder so it keeps its original file name (= package id).</summary>
    static string Slot(string staging) => Directory.CreateDirectory(Path.Combine(staging, Guid.NewGuid().ToString("N")[..8])).FullName;

    static void EnsureFreeSpace(string dir, long bytes)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(dir));
        if (root == null) return;
        var drive = new DriveInfo(root);
        const long margin = 256L << 20;
        if (drive.IsReady && drive.AvailableFreeSpace < bytes + margin)
            throw new IOException($"Not enough free space on {drive.Name} ({drive.AvailableFreeSpace >> 20} MB free, {bytes >> 20} MB needed).");
    }

    static bool IsArchiveName(string name) =>
        name.EndsWith(".scs", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    ModPackage ReadStaged(string path, ModMetadataCache cache)
    {
        bool isDir = Directory.Exists(path);
        var package = new ModPackage
        {
            PackageId = isDir ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path),
            Source = ModSource.Local,
            Kind = PackageReader.DetectKind(path),
            Path = path,
            Size = isDir ? 0 : new FileInfo(path).Length,
            LastWriteUtc = isDir ? Directory.GetLastWriteTimeUtc(path) : File.GetLastWriteTimeUtc(path),
        };
        try
        {
            using var reader = PackageReader.Open(path);
            var meta = ManifestReader.Read(reader);
            var key = ModMetadataCache.KeyFor(path, package.Size, package.LastWriteUtc);
            var icon = cache.Put(key, meta, null);
            return package with { Manifest = meta.Manifest, Description = meta.Description, IconPath = icon };
        }
        catch (Exception e) when (e is PackageFormatException or SiiFormatException or IOException)
        {
            return package with { Issues = [new ModIssue(ModIssueKind.Unreadable, IssueSeverity.Warning, "Could not read its manifest: " + e.Message)] };
        }
    }

    InstallCandidate Classify(ModPackage package, string source, ModCatalog catalog)
    {
        var name = Path.GetFileName(package.Path);
        var local = catalog.Packages.Where(p => !p.IsWorkshop).ToList();

        var sameId = local.FirstOrDefault(p => p.PackageId.Equals(package.PackageId, StringComparison.OrdinalIgnoreCase));
        if (sameId != null)
        {
            bool identical = !Directory.Exists(package.Path) && File.Exists(sameId.Path) && SameContent(package.Path, sameId.Path);
            return new InstallCandidate
            {
                Package = package, SourceDescription = source, TargetName = name, Existing = sameId,
                Conflict = identical ? InstallConflict.AlreadyInstalled : InstallConflict.SameFileName,
                Action = identical ? InstallAction.Skip : InstallAction.Replace,
            };
        }

        var other = local.FirstOrDefault(p => ModIdentity.IsSameMod(package, p));
        if (other != null)
        {
            var cmp = ModIdentity.Compare(package, other);
            return new InstallCandidate
            {
                Package = package, SourceDescription = source, TargetName = name, Existing = other,
                Conflict = cmp switch
                {
                    VersionComparison.Newer => InstallConflict.NewerVersionOf,
                    VersionComparison.Older => InstallConflict.OlderVersionOf,
                    _ => InstallConflict.OtherVersionOf,
                },
                // Replacing an older version is what people want; installing an older one next to a newer one is not.
                Action = cmp == VersionComparison.Older ? InstallAction.Skip : InstallAction.Replace,
            };
        }

        return new InstallCandidate { Package = package, SourceDescription = source, TargetName = name, Action = InstallAction.Install };
    }

    /// <summary>Same size and same hash of the first and last MiB: good enough for multi-GB mods.</summary>
    static bool SameContent(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        if (fa.Length != fb.Length) return false;
        return QuickHash(a) == QuickHash(b);
    }

    static string QuickHash(string path)
    {
        const int chunk = 1 << 20;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sha = SHA256.Create();
        var buf = new byte[chunk];
        int n = fs.Read(buf, 0, chunk);
        sha.TransformBlock(buf, 0, n, null, 0);
        if (fs.Length > chunk)
        {
            fs.Position = Math.Max(chunk, fs.Length - chunk);
            n = fs.Read(buf, 0, chunk);
            sha.TransformBlock(buf, 0, n, null, 0);
        }
        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }

    /// <summary>
    /// Moves the chosen candidates into the mod folder. Replaced mods are archived first; nothing is
    /// ever overwritten. The staging folder is removed afterwards.
    /// </summary>
    public IReadOnlyList<InstalledMod> Execute(InstallPlan plan, IProgress<string>? progress = null)
    {
        var results = new List<InstalledMod>();
        Directory.CreateDirectory(install.ModDir);
        try
        {
            foreach (var c in plan.Candidates.Where(c => c.Action != InstallAction.Skip))
            {
                progress?.Report(c.TargetName);
                ArchivedMod? replaced = null;
                if (c.Action == InstallAction.Replace && c.Existing != null)
                    replaced = archive.Archive(c.Existing.Path, $"Replaced by {c.TargetName}");

                var target = Path.Combine(install.ModDir, c.TargetName);
                if (File.Exists(target) || Directory.Exists(target))
                    throw new ModArchiveException($"{c.TargetName} already exists in the mod folder; it was not overwritten.");
                if (Directory.Exists(c.Package.Path)) Directory.Move(c.Package.Path, target);
                else File.Move(c.Package.Path, target);
                results.Add(new InstalledMod(c, target, replaced));
            }
        }
        finally
        {
            try { Directory.Delete(plan.StagingDir, recursive: true); } catch (IOException) { }
        }
        return results;
    }

    public void Cancel(InstallPlan plan)
    {
        try { Directory.Delete(plan.StagingDir, recursive: true); } catch (IOException) { }
    }

    static void CopyFile(string source, string target, CancellationToken ct)
    {
        EnsureFreeSpace(Path.GetDirectoryName(target)!, new FileInfo(source).Length);
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20);
        input.CopyToAsync(output, 1 << 20, ct).GetAwaiter().GetResult();
    }

    static void CopyDirectory(string source, string target, CancellationToken ct)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            CopyFile(file, dest, ct);
        }
    }
}
