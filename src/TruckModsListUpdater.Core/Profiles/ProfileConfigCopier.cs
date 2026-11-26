using TruckModsListUpdater.Core.Games;
using TruckModsListUpdater.Core.Platform;

namespace TruckModsListUpdater.Core.Profiles;

public class ConfigCopyException(string message, Exception? inner = null) : Exception(message, inner);

public enum ConfigFileAction
{
    /// <summary>The target does not have the file yet.</summary>
    Add,

    /// <summary>The target's file is different and will be replaced.</summary>
    Replace,

    /// <summary>The target's file is already identical.</summary>
    Unchanged,
}

/// <summary>One file of a copy, with the hashes the preview saw so a changed file is noticed before writing.</summary>
public sealed record ConfigFileCopy(
    ConfigCategory Category,
    string FileName,
    string SourcePath,
    string SourceSha256,
    ConfigRoot TargetRoot,
    string TargetPath,
    string? TargetSha256,
    ConfigFileAction Action);

/// <summary>What copying the chosen categories from one profile to another would do. Nothing is written yet.</summary>
public sealed record ConfigCopyPlan(
    GameInstallation Install,
    ProfileRef Source,
    ProfileRef Target,
    IReadOnlyList<ConfigCategory> Categories,
    IReadOnlyList<ConfigFileCopy> Files,
    IReadOnlyList<ConfigCategory> MissingInSource)
{
    public IReadOnlyList<ConfigFileCopy> Changes => Files.Where(f => f.Action != ConfigFileAction.Unchanged).ToList();
}

public sealed record ConfigCopyResult(ConfigBackup? Backup, IReadOnlyList<ConfigFileCopy> Written);

public sealed record ConfigRestoreResult(ConfigBackup? Backup, IReadOnlyList<string> Restored, IReadOnlyList<string> Skipped);

/// <summary>
/// Copies whole settings files (config.cfg, controls.sii, config_local.cfg, gearbox layouts) from one
/// profile to another of the same game. Every copy:
/// 1. refuses while the game runs (it saves its settings when it exits and would overwrite the copy);
/// 2. refuses when the source or target files changed since the preview;
/// 3. zips the target files it is about to change to the app's backup folder;
/// 4. writes each file to a temp file next to it, reads it back and compares it with the source;
/// 5. checks the game again and swaps the temp files in, putting back the files already
///    swapped if one fails.
/// Files the target has but the source does not are left alone, and profile.sii,
/// profile.bak.sii and saves are never touched.
/// </summary>
public sealed class ProfileConfigCopier(IGameProcessMonitor monitor, ConfigBackups backups)
{
    public const string TempSuffix = ".tmm.tmp";

    public static ConfigCopyPlan Plan(GameInstallation install, ProfileRef source, ProfileRef target, IEnumerable<ConfigCategory> categories)
    {
        if (SameProfile(source, target)) throw new ConfigCopyException("Choose two different profiles.");

        var chosen = categories.Distinct().OrderBy(c => c).ToList();
        var files = new List<ConfigFileCopy>();
        var missing = new List<ConfigCategory>();
        foreach (var category in chosen)
        {
            var sourceFiles = ProfileConfigs.Find(install, source, category);
            if (sourceFiles.Count == 0)
            {
                missing.Add(category);
                continue;
            }

            var targetFiles = ProfileConfigs.Find(install, target, category)
                .ToDictionary(f => f.FileName, StringComparer.OrdinalIgnoreCase);
            foreach (var file in sourceFiles)
            {
                var sourceHash = ProfileConfigs.Sha256OfFile(file.Path)
                    ?? throw new ConfigCopyException($"{file.FileName} disappeared from {source.DisplayName} while it was read.");
                ConfigRoot root;
                string targetPath;
                if (targetFiles.TryGetValue(file.FileName, out var existing))
                {
                    root = existing.Root;
                    targetPath = existing.Path;
                }
                else
                {
                    root = ProfileConfigs.DefaultRoot(target, category);
                    targetPath = Path.Combine(ProfileConfigs.RootDir(install, target, root), file.FileName);
                }

                var targetHash = ProfileConfigs.Sha256OfFile(targetPath);
                var action = targetHash == null ? ConfigFileAction.Add
                    : targetHash == sourceHash ? ConfigFileAction.Unchanged
                    : ConfigFileAction.Replace;
                files.Add(new ConfigFileCopy(category, file.FileName, file.Path, sourceHash, root, targetPath, targetHash, action));
            }
        }
        return new ConfigCopyPlan(install, source, target, chosen, files, missing);
    }

    public ConfigCopyResult Execute(ConfigCopyPlan plan)
    {
        var game = plan.Install.Game;
        var changes = plan.Changes;
        if (changes.Count == 0) return new ConfigCopyResult(null, []);
        EnsureGameClosed(game);

        // Read everything first, and make sure nothing changed since the preview.
        var newBytes = new List<byte[]>();
        var oldBytes = new List<byte[]?>();
        foreach (var f in changes)
        {
            var bytes = ReadShared(f.SourcePath)
                ?? throw new ConfigCopyException($"{f.FileName} is no longer in {plan.Source.DisplayName}. Check the profiles again and retry.");
            if (ProfileConfigs.Sha256(bytes) != f.SourceSha256) throw Changed(f.FileName, plan.Source);
            var before = ReadShared(f.TargetPath);
            if ((before == null ? null : ProfileConfigs.Sha256(before)) != f.TargetSha256) throw Changed(f.FileName, plan.Target);
            newBytes.Add(bytes);
            oldBytes.Add(before);
        }

        var description = $"Before copying {string.Join(", ", plan.Categories.Except(plan.MissingInSource).Select(ProfileConfigs.DisplayName))} from {plan.Source.DisplayName}";
        var backup = backups.Create(game, plan.Target, description,
            changes.Select((f, i) => new ConfigBackups.Item(f.TargetRoot, f.FileName, oldBytes[i], f.SourceSha256)).ToList());

        var writes = changes.Select((f, i) => new PendingWrite(f.TargetPath, newBytes[i], oldBytes[i], f.TargetSha256)).ToList();
        Write(game, writes);
        return new ConfigCopyResult(backup, changes);
    }

    /// <summary>
    /// Puts the files of a backup back. A file the copy added is removed again, unless it was changed
    /// since (then it is skipped and reported). The current files are backed up first.
    /// </summary>
    public ConfigRestoreResult Restore(GameInstallation install, ProfileRef profile, ConfigBackup backup)
    {
        var game = install.Game;
        EnsureGameClosed(game);
        var manifest = ConfigBackups.ReadManifest(backup.Path);
        if (!manifest.GameId.Equals(game.Id, StringComparison.OrdinalIgnoreCase)
            || !manifest.ProfileFolder.Equals(profile.FolderName, StringComparison.OrdinalIgnoreCase))
            throw new ConfigCopyException("This backup belongs to another profile.");

        var writes = new List<PendingWrite>();
        var roots = new List<ConfigRoot>();
        var restored = new List<string>();
        var skipped = new List<string>();
        foreach (var file in manifest.Files)
        {
            // Only names the copier itself writes are accepted, so a damaged manifest cannot reach other files.
            if (ProfileConfigs.CategoryOf(file.FileName) == null || Path.GetFileName(file.FileName) != file.FileName)
                throw new InvalidDataException($"The backup names an unexpected file: {file.FileName}");

            var path = Path.Combine(ProfileConfigs.RootDir(install, profile, file.Root), file.FileName);
            var current = ReadShared(path);
            var currentHash = current == null ? null : ProfileConfigs.Sha256(current);
            var wanted = file.Entry == null ? null : ConfigBackups.ReadEntry(backup.Path, file.Entry);

            if (wanted == null && current != null && currentHash != file.AfterSha256)
            {
                // The copy added this file and something changed it since: keep it.
                skipped.Add(file.FileName);
                continue;
            }
            if ((wanted == null ? null : ProfileConfigs.Sha256(wanted)) == currentHash) continue;
            writes.Add(new PendingWrite(path, wanted, current, currentHash));
            roots.Add(file.Root);
            restored.Add(file.FileName);
        }
        if (writes.Count == 0) return new ConfigRestoreResult(null, restored, skipped);

        var safety = backups.Create(game, profile, $"Before restoring the backup of {backup.CreatedLocal:g}",
            writes.Select((w, i) => new ConfigBackups.Item(roots[i], Path.GetFileName(w.Path), w.Before,
                w.Bytes == null ? null : ProfileConfigs.Sha256(w.Bytes))).ToList());

        Write(game, writes);
        return new ConfigRestoreResult(safety, restored, skipped);
    }

    /// <summary>A file to write (<see cref="Bytes"/> null = delete it), with what it holds now to undo a failed write.</summary>
    sealed record PendingWrite(string Path, byte[]? Bytes, byte[]? Before, string? BeforeSha256);

    void Write(GameDefinition game, IReadOnlyList<PendingWrite> writes)
    {
        var temps = new List<string>();
        var done = new List<PendingWrite>();
        try
        {
            foreach (var w in writes.Where(w => w.Bytes != null))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(w.Path)!);
                var tmp = w.Path + TempSuffix;
                temps.Add(tmp);
                File.WriteAllBytes(tmp, w.Bytes!);
                if (!File.ReadAllBytes(tmp).AsSpan().SequenceEqual(w.Bytes))
                    throw new ConfigCopyException($"{Path.GetFileName(w.Path)} could not be written correctly. Nothing was changed.");
            }

            EnsureGameClosed(game);
            foreach (var w in writes)
            {
                if (ProfileConfigs.Sha256OfFile(w.Path) != w.BeforeSha256)
                    throw new ConfigCopyException($"{Path.GetFileName(w.Path)} was changed by something else (the game or Steam). Check the profiles again and retry.");
                if (w.Bytes == null) File.Delete(w.Path);
                else File.Move(w.Path + TempSuffix, w.Path, overwrite: true);
                done.Add(w);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ConfigCopyException)
        {
            Undo(done);
            foreach (var t in temps) TryDelete(t);
            if (e is ConfigCopyException) throw;
            throw new ConfigCopyException($"The settings could not be written: {e.Message}{(done.Count > 0 ? " The files already changed were put back." : "")}", e);
        }
    }

    static void Undo(IEnumerable<PendingWrite> done)
    {
        foreach (var w in done.Reverse())
        {
            try
            {
                if (w.Before == null) File.Delete(w.Path);
                else File.WriteAllBytes(w.Path, w.Before);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    void EnsureGameClosed(GameDefinition game)
    {
        if (monitor.IsRunning(game))
            throw new ConfigCopyException($"{game.DisplayName} is running. Close the game first: it saves its settings when it exits and would overwrite the copy.");
    }

    static ConfigCopyException Changed(string fileName, ProfileRef profile) =>
        new($"{fileName} in {profile.DisplayName} changed after the preview. Check the profiles again and retry.");

    static byte[]? ReadShared(string path)
    {
        if (!File.Exists(path)) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    static bool SameProfile(ProfileRef a, ProfileRef b) =>
        a.Kind == b.Kind && a.FolderName.Equals(b.FolderName, StringComparison.OrdinalIgnoreCase);

    static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
