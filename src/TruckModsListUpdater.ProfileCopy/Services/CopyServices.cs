using System.Diagnostics;
using System.Windows;
using TruckModsListUpdater.Core.Games;
using TruckModsListUpdater.Core.Platform;
using TruckModsListUpdater.Core.Profiles;
using TruckModsListUpdater.Core.Storage;

namespace TruckModsListUpdater.ProfileCopy.Services;

/// <summary>The Core operations the window uses; an interface so the view model can be tested with fakes.</summary>
public interface ICopyServices
{
    IReadOnlyList<GameInstallation> LocateGames();
    IReadOnlyList<ProfileRef> Profiles(GameInstallation install);
    ProfileRef? LastPlayedProfile(GameInstallation install, IReadOnlyList<ProfileRef> profiles);
    bool IsGameRunning(GameDefinition game);
    ConfigCopyPlan Plan(GameInstallation install, ProfileRef source, ProfileRef target, IReadOnlyList<ConfigCategory> categories);
    ConfigCopyResult Copy(ConfigCopyPlan plan);
    IReadOnlyList<ConfigBackup> Backups(GameDefinition game, ProfileRef profile);
    ConfigRestoreResult Restore(GameInstallation install, ProfileRef profile, ConfigBackup backup);
    string BackupsDir(GameDefinition game, ProfileRef profile);
}

public sealed class CopyServices(AppPaths paths, IGameProcessMonitor monitor) : ICopyServices
{
    readonly ConfigBackups _backups = new(paths.ConfigBackupsDir);

    ProfileConfigCopier Copier => new(monitor, _backups);

    public IReadOnlyList<GameInstallation> LocateGames() =>
        GameDefinition.All.Select(g => GameLocator.Locate(g)).OfType<GameInstallation>().ToList();

    public IReadOnlyList<ProfileRef> Profiles(GameInstallation install) => ProfileStore.Enumerate(install);

    public ProfileRef? LastPlayedProfile(GameInstallation install, IReadOnlyList<ProfileRef> profiles) =>
        ProfileStore.FindActive(install, profiles);

    public bool IsGameRunning(GameDefinition game) => monitor.IsRunning(game);

    public ConfigCopyPlan Plan(GameInstallation install, ProfileRef source, ProfileRef target, IReadOnlyList<ConfigCategory> categories) =>
        ProfileConfigCopier.Plan(install, source, target, categories);

    public ConfigCopyResult Copy(ConfigCopyPlan plan) => Copier.Execute(plan);

    public IReadOnlyList<ConfigBackup> Backups(GameDefinition game, ProfileRef profile) => _backups.List(game, profile);

    public ConfigRestoreResult Restore(GameInstallation install, ProfileRef profile, ConfigBackup backup) =>
        Copier.Restore(install, profile, backup);

    public string BackupsDir(GameDefinition game, ProfileRef profile) => _backups.DirFor(game, profile);
}

/// <summary>Everything the view model asks or tells the user, so it can be tested without windows.</summary>
public interface IDialogs
{
    bool Confirm(string title, string message);
    void Error(string title, string message);
    void Info(string title, string message);
    void OpenFolder(string path);
}

public sealed class Dialogs : IDialogs
{
    static Window? Owner => Application.Current?.MainWindow;

    public bool Confirm(string title, string message) =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Error(string title, string message) => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void Info(string title, string message) => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void OpenFolder(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path)) return;
        var args = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true })?.Dispose();
    }

    static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image) =>
        Owner is { IsVisible: true } owner
            ? MessageBox.Show(owner, message, title, buttons, image)
            : MessageBox.Show(message, title, buttons, image);
}
