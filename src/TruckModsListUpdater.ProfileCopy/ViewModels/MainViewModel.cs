using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TruckModsListUpdater.Core.Games;
using TruckModsListUpdater.Core.Profiles;
using TruckModsListUpdater.Core.Storage;
using TruckModsListUpdater.ProfileCopy.Services;

namespace TruckModsListUpdater.ProfileCopy.ViewModels;

public sealed class ProfileOption(ProfileRef profile, bool isLastPlayed)
{
    public ProfileRef Profile { get; } = profile;
    public string DisplayName => Profile.DisplayName;
    public string Summary => $"{(Profile.IsCloud ? "Steam Cloud" : "Local")}{(isLastPlayed ? " · last played" : "")} · saved {Profile.LastWriteUtc.ToLocalTime():g}";
    public override string ToString() => DisplayName;
}

public sealed partial class CategoryOption(ConfigCategory category, Action changed) : ObservableObject
{
    public ConfigCategory Category { get; } = category;
    public string DisplayName => ProfileConfigs.DisplayName(Category);
    public string Description => ProfileConfigs.Description(Category);

    [ObservableProperty] private bool _isChecked = true;

    partial void OnIsCheckedChanged(bool value) => changed();
}

public sealed class FileRow(string category, string fileName, string action, string folder, bool isChange)
{
    public string Category { get; } = category;
    public string FileName { get; } = fileName;
    public string Action { get; } = action;
    public string Folder { get; } = folder;
    public bool IsChange { get; } = isChange;
}

public sealed class BackupOption(ConfigBackup backup)
{
    public ConfigBackup Backup { get; } = backup;
    public string Title => $"{Backup.CreatedLocal:g} — {Backup.Description}";
    public override string ToString() => Title;
}

public sealed partial class MainViewModel : ObservableObject
{
    readonly ICopyServices _services;
    readonly IDialogs _dialogs;
    ConfigCopyPlan? _plan;

    public MainViewModel(ICopyServices services, IDialogs dialogs)
    {
        _services = services;
        _dialogs = dialogs;
        Categories = ProfileConfigs.All.Select(c => new CategoryOption(c, RefreshPreview)).ToList();
    }

    public ObservableCollection<GameInstallation> Games { get; } = [];
    public ObservableCollection<ProfileOption> Profiles { get; } = [];
    public IReadOnlyList<CategoryOption> Categories { get; }
    public ObservableCollection<FileRow> Files { get; } = [];
    public ObservableCollection<BackupOption> Backups { get; } = [];

    [ObservableProperty] private GameInstallation? _selectedGame;
    [ObservableProperty] private ProfileOption? _source;
    [ObservableProperty] private ProfileOption? _target;
    [ObservableProperty] private BackupOption? _selectedBackup;
    [ObservableProperty] private bool _isGameRunning;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _statusText = "";

    public string Banner => IsGameRunning
        ? $"{SelectedGame?.Game.DisplayName ?? "The game"} is running. Close it to copy: the game saves its settings when it exits and would overwrite the copy."
        : "";

    public bool TargetIsCloud => Target?.Profile.IsCloud == true;

    partial void OnSelectedGameChanged(GameInstallation? value) => LoadProfiles();

    partial void OnSourceChanged(ProfileOption? value) => RefreshPreview();

    partial void OnTargetChanged(ProfileOption? value)
    {
        OnPropertyChanged(nameof(TargetIsCloud));
        LoadBackups();
        RefreshPreview();
    }

    partial void OnIsGameRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(Banner));
        NotifyCommands();
    }

    partial void OnIsBusyChanged(bool value) => NotifyCommands();
    partial void OnSelectedBackupChanged(BackupOption? value) => NotifyCommands();

    void NotifyCommands()
    {
        CopyCommand.NotifyCanExecuteChanged();
        RestoreCommand.NotifyCanExecuteChanged();
        SwapCommand.NotifyCanExecuteChanged();
    }

    // ---------------------------------------------------------------- loading

    public async Task InitializeAsync()
    {
        IsBusy = true;
        StatusText = "Looking for games…";
        try
        {
            var games = await Task.Run(_services.LocateGames);
            Games.Clear();
            foreach (var g in games) Games.Add(g);
        }
        finally
        {
            IsBusy = false;
        }

        if (Games.Count == 0)
        {
            StatusText = "No game found. Start Euro Truck Simulator 2 or American Truck Simulator once, then open this app again.";
            return;
        }
        SelectedGame = Games[0];
        StatusText = "";
    }

    void LoadProfiles()
    {
        Profiles.Clear();
        Source = null;
        Target = null;
        if (SelectedGame is not { } install) return;

        IsGameRunning = _services.IsGameRunning(install.Game);
        IReadOnlyList<ProfileRef> profiles;
        try
        {
            profiles = _services.Profiles(install);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Could not list profiles", e);
            _dialogs.Error("Profiles", e.Message);
            return;
        }

        var last = _services.LastPlayedProfile(install, profiles);
        foreach (var p in profiles) Profiles.Add(new ProfileOption(p, p == last));
        Source = Profiles.FirstOrDefault(p => p.Profile == last) ?? Profiles.FirstOrDefault();
        if (Profiles.Count < 2) StatusText = "This game has only one profile, so there is nothing to copy to.";
    }

    void LoadBackups()
    {
        Backups.Clear();
        if (SelectedGame is not { } install || Target is not { } target) return;
        foreach (var b in _services.Backups(install.Game, target.Profile)) Backups.Add(new BackupOption(b));
        SelectedBackup = Backups.FirstOrDefault();
    }

    // ---------------------------------------------------------------- preview

    void RefreshPreview()
    {
        _plan = null;
        Files.Clear();
        NotifyCommands();
        if (SelectedGame is not { } install || Source is not { } source || Target is not { } target)
        {
            Summary = "Choose the profile to copy from and the profile to copy to.";
            return;
        }
        if (source.Profile == target.Profile)
        {
            Summary = "Choose two different profiles.";
            return;
        }

        var chosen = Categories.Where(c => c.IsChecked).Select(c => c.Category).ToList();
        if (chosen.Count == 0)
        {
            Summary = "Tick at least one kind of setting to copy.";
            return;
        }

        try
        {
            _plan = _services.Plan(install, source.Profile, target.Profile, chosen);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ConfigCopyException)
        {
            AppLog.Warn("Preview failed: " + e.Message);
            Summary = e.Message;
            return;
        }

        foreach (var f in _plan.Files)
        {
            var action = f.Action switch
            {
                ConfigFileAction.Add => "New",
                ConfigFileAction.Replace => "Replace",
                _ => "Already the same",
            };
            var folder = f.TargetRoot == ConfigRoot.SteamProfiles ? "This PC (steam_profiles)" : target.Profile.IsCloud ? "Steam Cloud" : "Profile folder";
            Files.Add(new FileRow(ProfileConfigs.DisplayName(f.Category), f.FileName, action, folder, f.Action != ConfigFileAction.Unchanged));
        }
        foreach (var c in _plan.MissingInSource)
            Files.Add(new FileRow(ProfileConfigs.DisplayName(c), "—", $"Not in {source.DisplayName}, skipped", "", false));

        var changes = _plan.Changes.Count;
        Summary = changes == 0
            ? $"{target.DisplayName} already has the same settings. Nothing to copy."
            : $"{changes} file{(changes == 1 ? "" : "s")} will be copied to {target.DisplayName}. Its current files are backed up first.";
        NotifyCommands();
    }

    // ---------------------------------------------------------------- commands

    bool CanCopy() => _plan is { Changes.Count: > 0 } && !IsGameRunning && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanCopy))]
    async Task CopyAsync()
    {
        if (_plan is not { } plan) return;
        var count = plan.Changes.Count;
        var message = $"Copy {count} settings file{(count == 1 ? "" : "s")} from {plan.Source.DisplayName} to {plan.Target.DisplayName}?\n\n" +
                      $"The files of {plan.Target.DisplayName} that change are backed up first, and you can put them back with Restore.";
        if (plan.Target.IsCloud)
            message += "\n\nThis is a Steam Cloud profile. If Steam later reports a cloud conflict, keep the files on this PC.";
        if (!_dialogs.Confirm("Copy settings", message)) return;

        IsBusy = true;
        StatusText = "Copying…";
        try
        {
            var result = await Task.Run(() => _services.Copy(plan));
            AppLog.Info($"Copied {result.Written.Count} settings files from {plan.Source.Dir} to {plan.Target.Dir}");
            StatusText = $"Copied {result.Written.Count} file{(result.Written.Count == 1 ? "" : "s")} to {plan.Target.DisplayName}.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ConfigCopyException)
        {
            AppLog.Error("Copy failed", e);
            StatusText = "";
            _dialogs.Error("Copy settings", e.Message);
        }
        finally
        {
            IsBusy = false;
            LoadBackups();
            RefreshPreview();
        }
    }

    bool CanRestore() => SelectedBackup != null && Target != null && !IsGameRunning && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRestore))]
    async Task RestoreAsync()
    {
        if (SelectedGame is not { } install || Target is not { } target || SelectedBackup is not { } backup) return;
        if (!_dialogs.Confirm("Restore settings",
                $"Put back the settings of {target.DisplayName} from this backup?\n\n{backup.Title}\n\nThe current files are backed up first."))
            return;

        IsBusy = true;
        StatusText = "Restoring…";
        try
        {
            var result = await Task.Run(() => _services.Restore(install, target.Profile, backup.Backup));
            AppLog.Info($"Restored {result.Restored.Count} settings files of {target.Profile.Dir} from {backup.Backup.Path}");
            StatusText = result.Restored.Count == 0
                ? $"{target.DisplayName} already matches this backup."
                : $"Restored {result.Restored.Count} file{(result.Restored.Count == 1 ? "" : "s")} of {target.DisplayName}.";
            if (result.Skipped.Count > 0)
                _dialogs.Info("Restore settings",
                    "These files were added by the copy and changed since, so they were kept:\n\n" + string.Join("\n", result.Skipped));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or ConfigCopyException)
        {
            AppLog.Error("Restore failed", e);
            StatusText = "";
            _dialogs.Error("Restore settings", e.Message);
        }
        finally
        {
            IsBusy = false;
            LoadBackups();
            RefreshPreview();
        }
    }

    bool CanSwap() => Source != null && Target != null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSwap))]
    void Swap() => (Source, Target) = (Target, Source);

    [RelayCommand]
    void Refresh()
    {
        var source = Source?.Profile;
        var target = Target?.Profile;
        LoadProfiles();
        Source = Profiles.FirstOrDefault(p => SameDir(p.Profile, source)) ?? Source;
        Target = Profiles.FirstOrDefault(p => SameDir(p.Profile, target));
    }

    [RelayCommand]
    void OpenBackupsFolder()
    {
        if (SelectedGame is { } install && Target is { } target) _dialogs.OpenFolder(_services.BackupsDir(install.Game, target.Profile));
    }

    static bool SameDir(ProfileRef a, ProfileRef? b) => b != null && a.Dir.Equals(b.Dir, StringComparison.OrdinalIgnoreCase);

    /// <summary>Called on the UI thread when the game starts or exits.</summary>
    public void OnGameRunningChanged(GameDefinition game, bool running)
    {
        if (SelectedGame?.Game != game) return;
        IsGameRunning = running;
        // The game may have saved new settings when it closed.
        if (!running) RefreshPreview();
    }
}
