using TruckModsListUpdater.Core.Games;
using TruckModsListUpdater.Core.Mods;
using TruckModsListUpdater.Core.Platform;
using TruckModsListUpdater.Core.Presets;
using TruckModsListUpdater.Core.Profiles;
using TruckModsListUpdater.Core.Sii;
using TruckModsListUpdater.Core.Storage;

namespace TruckModsListUpdater.App.Services;

/// <summary>The Core operations the UI uses; an interface so view models can be tested with fakes.</summary>
public interface IGameServices
{
    IReadOnlyList<GameInstallation> LocateGames();
    Task<ModCatalog> ScanAsync(GameInstallation install, IProgress<string>? progress = null);
    IReadOnlyList<ProfileRef> Profiles(GameInstallation install);
    ProfileRef? LastPlayedProfile(GameInstallation install, IReadOnlyList<ProfileRef> profiles);
    ProfileSession LoadProfile(ProfileRef profile);
    bool IsGameRunning(GameDefinition game);
    bool IsSteamRunning();
    void Launch(GameDefinition game);
    ProfileWriteResult WriteActiveMods(GameDefinition game, ProfileSession session, IReadOnlyList<ActiveModEntry> fileOrder);
    IReadOnlyList<ProfileBackup> Backups(GameDefinition game, ProfileRef profile);
    ProfileWriteResult RestoreBackup(GameDefinition game, ProfileRef profile, ProfileBackup backup);

    Task<InstallPlan> AnalyzeInstallAsync(GameInstallation install, IReadOnlyList<string> paths, ModCatalog catalog);
    Task<IReadOnlyList<InstalledMod>> ExecuteInstallAsync(GameInstallation install, InstallPlan plan, IProgress<string>? progress = null);
    void CancelInstall(GameInstallation install, InstallPlan plan);
    IReadOnlyList<ProfileRef> UpdateReferences(GameDefinition game, IEnumerable<ProfileRef> profiles, IReadOnlyCollection<ModReplacement> replacements);

    ArchivedMod ArchiveMod(GameInstallation install, string path, string reason);
    IReadOnlyList<ArchivedMod> ArchivedMods(GameInstallation install);
    void RestoreArchivedMod(GameInstallation install, ArchivedMod item);

    PresetStore Presets { get; }
}

public sealed class GameServices(AppPaths paths, AppSettings settings, IGameProcessMonitor monitor) : IGameServices
{
    readonly ModMetadataCache _cache = LoadCache(paths);
    readonly ProfileBackups _backups = new(paths.BackupsDir);

    public PresetStore Presets { get; } = new(paths.PresetsDir);

    static ModMetadataCache LoadCache(AppPaths paths)
    {
        var cache = new ModMetadataCache(paths.CacheDir);
        cache.Load();
        return cache;
    }

    public IReadOnlyList<GameInstallation> LocateGames() =>
        GameDefinition.All.Select(g => GameLocator.Locate(g, g.Id == settings.LastGameId ? settings.DocumentsOverride : null))
            .OfType<GameInstallation>().ToList();

    public async Task<ModCatalog> ScanAsync(GameInstallation install, IProgress<string>? progress = null)
    {
        var catalog = await Task.Run(() => new ModScanner(_cache).ScanAsync(install, progress));
        try { _cache.Save(); } catch (IOException e) { AppLog.Warn("Could not save mod cache: " + e.Message); }
        return catalog;
    }

    public IReadOnlyList<ProfileRef> Profiles(GameInstallation install) => ProfileStore.Enumerate(install);

    public ProfileRef? LastPlayedProfile(GameInstallation install, IReadOnlyList<ProfileRef> profiles) =>
        ProfileStore.FindActive(install, profiles);

    public ProfileSession LoadProfile(ProfileRef profile)
    {
        ProfileWriter.CleanUpTempFiles(profile);
        return ProfileSession.Load(profile);
    }

    public bool IsGameRunning(GameDefinition game) => monitor.IsRunning(game);
    public bool IsSteamRunning() => GameLauncher.IsSteamRunning();
    public void Launch(GameDefinition game) => GameLauncher.Launch(game);

    public ProfileWriteResult WriteActiveMods(GameDefinition game, ProfileSession session, IReadOnlyList<ActiveModEntry> fileOrder) =>
        new ProfileWriter(monitor, _backups).WriteActiveMods(game, session, fileOrder);

    public IReadOnlyList<ProfileBackup> Backups(GameDefinition game, ProfileRef profile) => _backups.List(game, profile);

    public ProfileWriteResult RestoreBackup(GameDefinition game, ProfileRef profile, ProfileBackup backup) =>
        new ProfileWriter(monitor, _backups).Restore(game, profile, backup);

    static ModArchive Archive(GameInstallation install) => new(install.ArchiveDir);

    public Task<InstallPlan> AnalyzeInstallAsync(GameInstallation install, IReadOnlyList<string> paths, ModCatalog catalog) =>
        new ModInstaller(install, Archive(install)).AnalyzeAsync(paths, catalog);

    public Task<IReadOnlyList<InstalledMod>> ExecuteInstallAsync(GameInstallation install, InstallPlan plan, IProgress<string>? progress = null) =>
        Task.Run(() => new ModInstaller(install, Archive(install)).Execute(plan, progress));

    public void CancelInstall(GameInstallation install, InstallPlan plan) => new ModInstaller(install, Archive(install)).Cancel(plan);

    public IReadOnlyList<ProfileRef> UpdateReferences(GameDefinition game, IEnumerable<ProfileRef> profiles, IReadOnlyCollection<ModReplacement> replacements) =>
        ProfileReferenceUpdater.UpdateProfiles(game, profiles, replacements, new ProfileWriter(monitor, _backups)).Select(u => u.Profile).ToList();

    public ArchivedMod ArchiveMod(GameInstallation install, string path, string reason) => Archive(install).Archive(path, reason);

    public IReadOnlyList<ArchivedMod> ArchivedMods(GameInstallation install) => Archive(install).List();

    public void RestoreArchivedMod(GameInstallation install, ArchivedMod item) => Archive(install).Restore(item);
}
