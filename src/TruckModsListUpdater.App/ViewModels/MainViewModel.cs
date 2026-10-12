using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GongSolutions.Wpf.DragDrop;
using TruckModsListUpdater.App.Services;
using TruckModsListUpdater.Core.Games;
using TruckModsListUpdater.Core.Mods;
using TruckModsListUpdater.Core.Presets;
using TruckModsListUpdater.Core.Profiles;
using TruckModsListUpdater.Core.Sii;
using TruckModsListUpdater.Core.Storage;

namespace TruckModsListUpdater.App.ViewModels;

public enum ModFilter { All, Local, Workshop, WithIssues }

public sealed partial class MainViewModel : ObservableObject, IDropTarget
{
    readonly IGameServices _services;
    readonly IUserDialogs _dialogs;
    readonly AppSettings _settings;
    readonly Action _saveSettings;

    ModCatalog? _catalog;
    ProfileSession? _session;
    Dictionary<string, string> _nameHints = new(StringComparer.OrdinalIgnoreCase);
    bool _suppressProfileChange;

    public MainViewModel(IGameServices services, IUserDialogs dialogs, AppSettings settings, Action saveSettings)
    {
        _services = services;
        _dialogs = dialogs;
        _settings = settings;
        _saveSettings = saveSettings;
        AvailableView = CollectionViewSource.GetDefaultView(AvailableMods);
        AvailableView.Filter = FilterAvailable;
        AvailableView.SortDescriptions.Add(new SortDescription(nameof(ModItemViewModel.DisplayName), ListSortDirection.Ascending));
        ActiveMods.CollectionChanged += (_, _) => UpdateChanges();
    }

    public ObservableCollection<GameInstallation> Games { get; } = [];
    public ObservableCollection<ProfileItemViewModel> Profiles { get; } = [];

    /// <summary>Active mods in UI order: top = highest priority (as in the in-game Mod Manager).</summary>
    public ObservableCollection<ModItemViewModel> ActiveMods { get; } = [];
    public ObservableCollection<ModItemViewModel> AvailableMods { get; } = [];
    public ICollectionView AvailableView { get; }
    public IReadOnlyList<ModFilter> Filters { get; } = Enum.GetValues<ModFilter>();

    [ObservableProperty] private GameInstallation? _selectedGame;
    [ObservableProperty] private ProfileItemViewModel? _selectedProfile;
    [ObservableProperty] private ModItemViewModel? _selectedMod;
    [ObservableProperty] private ModItemViewModel? _selectedActiveMod;
    [ObservableProperty] private ModItemViewModel? _selectedAvailableMod;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private ModFilter _filter = ModFilter.All;
    [ObservableProperty] private bool _isGameRunning;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasChanges;
    [ObservableProperty] private string _statusText = "";

    public bool IsReadOnlyProfile => _session is { IsWritable: false };
    public bool IsCloudProfile => SelectedProfile?.Profile.IsCloud == true;
    public string ActiveHeader => $"Active mods ({ActiveMods.Count}) — top has the highest priority";
    public string AvailableHeader => $"Available mods ({AvailableMods.Count})";
    public string GameVersionText => SelectedGame?.GameVersion is { } v ? $"Game {v}" : "";

    public string Banner => IsGameRunning
        ? $"{SelectedGame?.Game.DisplayName ?? "The game"} is running. Close it to apply changes: the game rewrites the profile when it exits."
        : IsReadOnlyProfile
            ? "This profile is saved in binary format and can only be viewed."
            : "";

    partial void OnSearchTextChanged(string value) => AvailableView.Refresh();
    partial void OnFilterChanged(ModFilter value) => AvailableView.Refresh();

    partial void OnSelectedActiveModChanged(ModItemViewModel? value)
    {
        if (value != null) SelectedMod = value;
        NotifyCommands();
    }

    partial void OnSelectedAvailableModChanged(ModItemViewModel? value)
    {
        if (value != null) SelectedMod = value;
        NotifyCommands();
    }

    partial void OnIsGameRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(Banner));
        NotifyCommands();
    }

    partial void OnIsBusyChanged(bool value) => NotifyCommands();
    partial void OnHasChangesChanged(bool value) => NotifyCommands();

    // ---------------------------------------------------------------- loading

    public async Task InitializeAsync()
    {
        IsBusy = true;
        StatusText = "Looking for games…";
        IReadOnlyList<GameInstallation> games;
        try
        {
            games = await Task.Run(_services.LocateGames);
        }
        finally
        {
            IsBusy = false;
        }

        Games.Clear();
        foreach (var g in games) Games.Add(g);
        if (Games.Count == 0)
        {
            StatusText = "No Euro Truck Simulator 2 or American Truck Simulator documents folder was found.";
            _dialogs.Error("Game not found",
                "Could not find the game's documents folder (Documents\\Euro Truck Simulator 2). Start the game once so it creates its folders, then restart Modshift.");
            return;
        }
        SelectedGame = Games.FirstOrDefault(g => g.Game.Id == _settings.LastGameId) ?? Games[0];
        await CurrentLoad;
    }

    /// <summary>The load started by the last game selection (awaitable for tests and startup).</summary>
    internal Task CurrentLoad { get; private set; } = Task.CompletedTask;

    partial void OnSelectedGameChanged(GameInstallation? value)
    {
        OnPropertyChanged(nameof(GameVersionText));
        if (value != null) CurrentLoad = LoadGameAsync(value);
    }

    async Task LoadGameAsync(GameInstallation install)
    {
        IsBusy = true;
        try
        {
            IsGameRunning = _services.IsGameRunning(install.Game);
            StatusText = "Scanning mods…";
            var progress = new Progress<string>(name => StatusText = $"Reading {name}…");
            _catalog = await _services.ScanAsync(install, progress);

            var profiles = _services.Profiles(install);
            var lastPlayed = _services.LastPlayedProfile(install, profiles);
            var items = new List<ProfileItemViewModel>();
            var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in profiles)
            {
                int count = 0;
                try
                {
                    var s = _services.LoadProfile(p);
                    count = s.ActiveMods.Count;
                    // The game stores each workshop mod's title in the profile; use it where the package has no name.
                    foreach (var m in s.ActiveMods) hints.TryAdd(m.PackageId, m.DisplayName);
                }
                catch (Exception e) when (e is SiiFormatException or IOException or UnauthorizedAccessException)
                {
                    AppLog.Warn($"Could not read profile {p.Dir}: {e.Message}");
                }
                items.Add(new ProfileItemViewModel(p, p == lastPlayed, count));
            }
            _nameHints = hints;

            _suppressProfileChange = true;
            Profiles.Clear();
            foreach (var i in items) Profiles.Add(i);
            _suppressProfileChange = false;

            _settings.LastGameId = install.Game.Id;
            var select = items.FirstOrDefault(i => i.Profile.Dir == _settings.LastProfileDir)
                ?? items.FirstOrDefault(i => i.IsActive)
                ?? items.FirstOrDefault();
            SelectedProfile = select;
            if (select == null) RebuildLists([]);
            StatusText = $"{_catalog.Packages.Count} mods found, {profiles.Count} profiles.";
        }
        catch (Exception e)
        {
            AppLog.Error("Loading game failed", e);
            StatusText = "Loading failed: " + e.Message;
            _dialogs.Error("Loading failed", e.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedProfileChanging(ProfileItemViewModel? oldValue, ProfileItemViewModel? newValue)
    {
        // Ask before throwing away edits; if cancelled, the selection is restored in OnSelectedProfileChanged.
        if (_suppressProfileChange || !HasChanges || oldValue == null || newValue == oldValue) return;
        var choice = _dialogs.AskSaveChanges($"Save the changes to the mod list of \"{oldValue.DisplayName}\" first?");
        if (choice == SaveChoice.Save) Apply();
        _pendingProfileRevert = choice == SaveChoice.Cancel ? oldValue : null;
    }

    ProfileItemViewModel? _pendingProfileRevert;

    partial void OnSelectedProfileChanged(ProfileItemViewModel? value)
    {
        OnPropertyChanged(nameof(IsCloudProfile));
        if (_suppressProfileChange) return;
        if (_pendingProfileRevert is { } revert)
        {
            _pendingProfileRevert = null;
            // Defer: changing the selection inside its own change notification confuses the ComboBox.
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                _suppressProfileChange = true;
                SelectedProfile = revert;
                _suppressProfileChange = false;
            });
            return;
        }
        if (value != null) LoadProfile(value.Profile);
    }

    void LoadProfile(ProfileRef profile)
    {
        try
        {
            _session = _services.LoadProfile(profile);
            _settings.LastProfileDir = profile.Dir;
            _saveSettings();
            RebuildLists(_session.ActiveMods);
            StatusText = $"Loaded profile \"{profile.DisplayName}\" ({_session.ActiveMods.Count} active mods).";
        }
        catch (Exception e) when (e is SiiFormatException or IOException or UnauthorizedAccessException)
        {
            _session = null;
            RebuildLists([]);
            AppLog.Error($"Could not load profile {profile.Dir}", e);
            _dialogs.Error("Could not read profile", e.Message);
        }
        OnPropertyChanged(nameof(IsReadOnlyProfile));
        OnPropertyChanged(nameof(Banner));
    }

    /// <summary>Fills both lists from a mod list in file order.</summary>
    void RebuildLists(IReadOnlyList<ActiveModEntry> fileOrder)
    {
        var install = SelectedGame;
        ActiveMods.Clear();
        AvailableMods.Clear();
        SelectedMod = null;
        if (install == null || _catalog == null) return;

        var activeIds = new HashSet<string>(fileOrder.Select(e => e.PackageId), StringComparer.OrdinalIgnoreCase);
        foreach (var entry in LoadOrderMapper.ToDisplayOrder(fileOrder))
            ActiveMods.Add(new ModItemViewModel(_catalog.Find(entry.PackageId), entry, install, null));
        foreach (var package in _catalog.Packages.Where(p => !activeIds.Contains(p.PackageId)))
            AvailableMods.Add(new ModItemViewModel(package, null, install, _nameHints.GetValueOrDefault(package.PackageId)));
        UpdateChanges();
    }

    bool FilterAvailable(object o)
    {
        if (o is not ModItemViewModel m) return false;
        if (!string.IsNullOrWhiteSpace(SearchText) && !m.SearchText.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase))
            return false;
        return Filter switch
        {
            ModFilter.Local => !m.IsWorkshop,
            ModFilter.Workshop => m.IsWorkshop,
            ModFilter.WithIssues => m.HasWarnings,
            _ => true,
        };
    }

    void UpdateChanges()
    {
        var saved = _session == null ? Enumerable.Empty<string>() : LoadOrderMapper.ToDisplayOrder(_session.ActiveMods).Select(e => e.RawValue);
        HasChanges = _session != null && !ActiveMods.Select(m => m.Entry.RawValue).SequenceEqual(saved);
        OnPropertyChanged(nameof(ActiveHeader));
        OnPropertyChanged(nameof(AvailableHeader));
        NotifyCommands();
    }

    void NotifyCommands()
    {
        EnableCommand.NotifyCanExecuteChanged();
        DisableCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        MoveToTopCommand.NotifyCanExecuteChanged();
        MoveToBottomCommand.NotifyCanExecuteChanged();
        ApplyCommand.NotifyCanExecuteChanged();
        RevertCommand.NotifyCanExecuteChanged();
        FixMissingModsCommand.NotifyCanExecuteChanged();
        ShowPresetsCommand.NotifyCanExecuteChanged();
    }

    // ---------------------------------------------------------------- editing

    bool CanEdit => _session is { IsWritable: true } && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanEnable))]
    void Enable(ModItemViewModel? mod) => EnableAt(mod ?? SelectedAvailableMod, 0);

    bool CanEnable(ModItemViewModel? mod) => CanEdit && (mod ?? SelectedAvailableMod) != null;

    void EnableAt(ModItemViewModel? mod, int index)
    {
        if (mod == null || !CanEdit || !AvailableMods.Contains(mod)) return;
        AvailableMods.Remove(mod);
        ActiveMods.Insert(Math.Clamp(index, 0, ActiveMods.Count), mod);
        SelectedActiveMod = mod;
    }

    [RelayCommand(CanExecute = nameof(CanDisable))]
    void Disable(ModItemViewModel? mod)
    {
        mod ??= SelectedActiveMod;
        if (mod == null || !CanEdit || !ActiveMods.Contains(mod)) return;
        int index = ActiveMods.IndexOf(mod);
        ActiveMods.Remove(mod);
        // Missing packages cannot be re-enabled from the library, so they simply disappear.
        if (!mod.IsMissing && SelectedGame != null)
            AvailableMods.Add(new ModItemViewModel(mod.Package, null, SelectedGame, mod.DisplayName));
        SelectedActiveMod = ActiveMods.Count == 0 ? null : ActiveMods[Math.Min(index, ActiveMods.Count - 1)];
    }

    bool CanDisable(ModItemViewModel? mod) => CanEdit && (mod ?? SelectedActiveMod) != null;

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    void MoveUp() => Move(SelectedActiveMod, -1);

    bool CanMoveUp() => CanEdit && SelectedActiveMod != null && ActiveMods.IndexOf(SelectedActiveMod) > 0;

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    void MoveDown() => Move(SelectedActiveMod, +1);

    bool CanMoveDown() => CanEdit && SelectedActiveMod != null && ActiveMods.IndexOf(SelectedActiveMod) is var i && i >= 0 && i < ActiveMods.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    void MoveToTop() => Move(SelectedActiveMod, -ActiveMods.Count);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    void MoveToBottom() => Move(SelectedActiveMod, ActiveMods.Count);

    void Move(ModItemViewModel? mod, int delta)
    {
        if (mod == null) return;
        int from = ActiveMods.IndexOf(mod);
        int to = Math.Clamp(from + delta, 0, ActiveMods.Count - 1);
        if (from < 0 || from == to) return;
        ActiveMods.Move(from, to);
        SelectedActiveMod = mod;
        NotifyCommands();
    }

    [RelayCommand(CanExecute = nameof(CanRevert))]
    void Revert()
    {
        if (_session != null) RebuildLists(_session.ActiveMods);
        StatusText = "Changes discarded.";
    }

    bool CanRevert() => HasChanges && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanApply))]
    void Apply()
    {
        if (_session == null || SelectedGame == null) return;
        if (_session.Profile.IsCloud && !_settings.CloudWarningAcknowledged)
        {
            if (!_dialogs.Confirm("Steam Cloud profile",
                    "This profile is synchronised by Steam Cloud. Modshift changes the file Steam syncs; " +
                    "if Steam later reports a cloud conflict, keep the local files.\n\nA backup is made before every change. Continue?"))
                return;
            _settings.CloudWarningAcknowledged = true;
            _saveSettings();
        }

        try
        {
            var fileOrder = LoadOrderMapper.ToFileOrder(ActiveMods.Select(m => m.Entry));
            var result = _services.WriteActiveMods(SelectedGame.Game, _session, fileOrder);
            _session = result.Session;
            AppLog.Info($"Wrote {fileOrder.Count} active mods to {_session.Profile.SiiPath}; backup {result.Backup.Path}");
            RebuildListsKeepingSelection();
            StatusText = $"Saved {fileOrder.Count} mods to \"{_session.Profile.DisplayName}\". Backup: {result.Backup.Name}";
        }
        catch (ProfileChangedException e)
        {
            AppLog.Warn(e.Message);
            if (_dialogs.Confirm("Profile changed", e.Message + "\n\nReload it now? Your unsaved changes will be lost."))
                LoadProfile(_session.Profile);
        }
        catch (Exception e) when (e is ProfileWriteException or IOException or UnauthorizedAccessException or SiiFormatException)
        {
            AppLog.Error("Saving profile failed", e);
            _dialogs.Error("Could not save", e.Message);
        }
    }

    bool CanApply() => HasChanges && !IsGameRunning && CanEdit;

    void RebuildListsKeepingSelection()
    {
        var selectedId = SelectedMod?.PackageId;
        if (_session != null) RebuildLists(_session.ActiveMods);
        SelectedMod = ActiveMods.Concat(AvailableMods).FirstOrDefault(m => m.PackageId == selectedId);
    }

    // ---------------------------------------------------------------- other commands

    [RelayCommand]
    void Launch()
    {
        if (SelectedGame == null) return;
        if (HasChanges)
        {
            switch (_dialogs.AskSaveChanges("Save the mod list before starting the game?"))
            {
                case SaveChoice.Cancel: return;
                case SaveChoice.Save:
                    Apply();
                    if (HasChanges) return; // saving failed
                    break;
            }
        }
        _services.Launch(SelectedGame.Game);
        StatusText = $"Starting {SelectedGame.Game.DisplayName} through Steam…";
    }

    [RelayCommand]
    async Task RescanAsync()
    {
        if (SelectedGame == null || _catalog == null) return;
        IsBusy = true;
        try
        {
            var working = LoadOrderMapper.ToFileOrder(ActiveMods.Select(m => m.Entry));
            _catalog = await _services.ScanAsync(SelectedGame, new Progress<string>(n => StatusText = $"Reading {n}…"));
            var hadChanges = HasChanges;
            RebuildLists(working);
            StatusText = $"{_catalog.Packages.Count} mods found.";
            if (!hadChanges) UpdateChanges();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    void OpenModFolder()
    {
        if (SelectedGame != null) _dialogs.OpenFolder(SelectedGame.ModDir);
    }

    [RelayCommand]
    void OpenProfileFolder()
    {
        if (SelectedProfile != null) _dialogs.OpenFolder(SelectedProfile.Profile.Dir);
    }

    [RelayCommand]
    void ShowInExplorer(ModItemViewModel? mod)
    {
        if ((mod ?? SelectedMod)?.Path is { } path) _dialogs.OpenFolder(path);
    }

    /// <summary>Re-reads the backup list (called when the Restore menu opens).</summary>
    public void RefreshBackups() => OnPropertyChanged(nameof(Backups));

    public IReadOnlyList<ProfileBackup> Backups =>
        SelectedGame != null && SelectedProfile != null ? _services.Backups(SelectedGame.Game, SelectedProfile.Profile) : [];

    [RelayCommand]
    void RestoreBackup(ProfileBackup? backup)
    {
        if (backup == null || SelectedGame == null || SelectedProfile == null) return;
        if (!_dialogs.Confirm("Restore backup",
                $"Replace the profile file of \"{SelectedProfile.DisplayName}\" with the backup from {backup.CreatedLocal:g}?\n\nThe current state is backed up first."))
            return;
        try
        {
            var result = _services.RestoreBackup(SelectedGame.Game, SelectedProfile.Profile, backup);
            _session = result.Session;
            RebuildLists(_session.ActiveMods);
            StatusText = $"Restored backup from {backup.CreatedLocal:g}.";
            AppLog.Info($"Restored {backup.Path} to {SelectedProfile.Profile.SiiPath}");
        }
        catch (Exception e) when (e is ProfileWriteException or IOException or InvalidDataException or SiiFormatException)
        {
            AppLog.Error("Restore failed", e);
            _dialogs.Error("Could not restore", e.Message);
        }
    }

    /// <summary>Called (on the UI thread) when the game starts or exits.</summary>
    public void OnGameRunningChanged(GameDefinition game, bool running)
    {
        if (SelectedGame?.Game != game) return;
        IsGameRunning = running;
        if (running || SelectedProfile == null) return;

        // The game just rewrote the profile; pick up its version unless the user has edits pending.
        if (!HasChanges) LoadProfile(SelectedProfile.Profile);
        else StatusText = "The game has closed and may have changed the profile. Apply will check before overwriting.";
    }

    /// <summary>Returns false when closing should be cancelled.</summary>
    public bool ConfirmClose()
    {
        if (!HasChanges) return true;
        switch (_dialogs.AskSaveChanges("Save the changes to the mod list before closing?"))
        {
            case SaveChoice.Save:
                Apply();
                return !HasChanges;
            case SaveChoice.Discard:
                return true;
            default:
                return false;
        }
    }

    // ---------------------------------------------------------------- presets and fixing missing mods

    [RelayCommand(CanExecute = nameof(CanUsePresets))]
    void ShowPresets()
    {
        if (SelectedGame == null || SelectedProfile == null) return;
        var vm = new PresetsViewModel(_services.Presets, _dialogs, SelectedGame.Game.Id, SelectedProfile.DisplayName,
            name => ModPreset.FromFileOrder(name, SelectedGame.Game.Id, LoadOrderMapper.ToFileOrder(ActiveMods.Select(m => m.Entry)), _catalog),
            ApplyPreset);
        _dialogs.ShowPresets(vm);
    }

    bool CanUsePresets() => CanEdit && _catalog != null;

    /// <summary>Replaces the edited mod list with a preset; the user still reviews and applies it.</summary>
    public void ApplyPreset(ModPreset preset)
    {
        if (_catalog == null || !CanEdit) return;
        var result = PresetApplier.Resolve(preset, _catalog);
        RebuildLists(result.FileOrder);
        StatusText = $"Preset \"{preset.Name}\" loaded ({result.FileOrder.Count} mods). Review the list and click Apply to save it.";

        var notes = new List<string>();
        if (result.Substituted.Count > 0)
            notes.Add("Installed versions used instead:\n" + string.Join("\n", result.Substituted.Select(s => $"  {s.Entry.PackageId} → {s.Package.PackageId}")));
        if (result.Missing.Count > 0)
            notes.Add("Not installed, left out:\n" + string.Join("\n", result.Missing.Select(m => $"  {m.DisplayName} ({m.PackageId})")));
        if (notes.Count > 0) _dialogs.Info($"Preset \"{preset.Name}\"", string.Join("\n\n", notes));
    }

    [RelayCommand(CanExecute = nameof(CanFixMissing))]
    void FixMissingMods()
    {
        if (_catalog == null) return;
        var suggestions = MissingModResolver.Suggest(ActiveMods.Select(m => m.Entry), _catalog);
        if (suggestions.Count == 0)
        {
            _dialogs.Info("Fix missing mods", "No installed replacements were found for the missing mods. Install them first (for example with Install mods…).");
            return;
        }
        var text = string.Join("\n", suggestions.Select(s => $"{s.Missing.PackageId}  →  {s.Replacement.PackageId}"));
        if (!_dialogs.Confirm("Fix missing mods", $"Replace these missing mods with the installed versions, keeping their place in the load order?\n\n{text}"))
            return;

        var replacements = suggestions.Select(s => new ModReplacement(s.Missing.PackageId, s.Replacement.PackageId, s.Replacement.DisplayName)).ToList();
        RebuildLists(ProfileReferenceUpdater.Apply(LoadOrderMapper.ToFileOrder(ActiveMods.Select(m => m.Entry)), replacements));
        StatusText = $"Replaced {replacements.Count} missing mod(s). Click Apply to save.";
    }

    bool CanFixMissing() => CanEdit && ActiveMods.Any(m => m.IsMissing);

    // ---------------------------------------------------------------- install, archive, clean up

    [RelayCommand]
    async Task InstallModsAsync()
    {
        if (_dialogs.PickModFiles() is { Count: > 0 } files) await InstallFilesAsync(files);
    }

    /// <summary>Installs dropped or picked files: analyse, preview, copy into the mod folder, rescan.</summary>
    public async Task InstallFilesAsync(IReadOnlyList<string> paths)
    {
        if (SelectedGame is not { } install || _catalog == null || IsBusy) return;
        IsBusy = true;
        try
        {
            StatusText = "Reading dropped files…";
            var plan = await _services.AnalyzeInstallAsync(install, paths, _catalog);
            if (plan.Candidates.Count == 0)
            {
                _services.CancelInstall(install, plan);
                StatusText = "No mods found.";
                _dialogs.Error("No mods found", string.Join("\n", plan.Problems.DefaultIfEmpty("The dropped files contain no .scs or .zip mods.")));
                return;
            }

            var preview = new InstallViewModel(plan, _session?.IsWritable == true ? SelectedProfile?.DisplayName : null);
            if (!_dialogs.ShowInstall(preview) || !preview.HasWork)
            {
                _services.CancelInstall(install, plan);
                StatusText = "Install cancelled.";
                return;
            }

            StatusText = "Installing…";
            var installed = await _services.ExecuteInstallAsync(install, plan, new Progress<string>(n => StatusText = $"Installing {n}…"));
            AppLog.Info($"Installed {string.Join(", ", installed.Select(i => i.InstalledPath))}");

            // New file names for replaced versions: point profiles at the new package id.
            var replacements = installed
                .Where(i => i.Candidate.Existing is { } old && !old.PackageId.Equals(i.Candidate.Package.PackageId, StringComparison.OrdinalIgnoreCase)
                            && i.Candidate.Action == InstallAction.Replace)
                .Select(i => new ModReplacement(i.Candidate.Existing!.PackageId, i.Candidate.Package.PackageId, i.Candidate.Package.DisplayName))
                .ToList();

            var working = LoadOrderMapper.ToFileOrder(ActiveMods.Select(m => m.Entry)).ToList();
            var hadChanges = HasChanges;
            var updatedProfiles = new List<string>();
            if (preview.UpdateProfiles && replacements.Count > 0)
            {
                // The open profile is updated in the editor (the user applies it); the others are written now.
                working = ProfileReferenceUpdater.Apply(working, replacements).ToList();
                var others = Profiles.Select(p => p.Profile).Where(p => p != SelectedProfile?.Profile);
                try
                {
                    updatedProfiles.AddRange(_services.UpdateReferences(install.Game, others, replacements).Select(p => p.DisplayName));
                }
                catch (Exception e) when (e is ProfileWriteException or IOException or SiiFormatException)
                {
                    AppLog.Error("Updating other profiles failed", e);
                    _dialogs.Error("Other profiles not updated", e.Message);
                }
            }

            if (preview.EnableInProfile)
            {
                // New mods go to the top (highest priority), in the order they were listed.
                var fresh = installed.Where(i => i.Candidate.Existing == null)
                    .Select(i => ActiveModEntry.Create(i.Candidate.Package.PackageId, i.Candidate.Package.DisplayName))
                    .Where(e => working.All(w => !w.PackageId.Equals(e.PackageId, StringComparison.OrdinalIgnoreCase)));
                working.AddRange(fresh.Reverse());
            }

            _catalog = await _services.ScanAsync(install);
            RebuildLists(working);
            if (!hadChanges) UpdateChanges();

            StatusText = $"Installed {installed.Count} mod(s)."
                + (updatedProfiles.Count > 0 ? $" Updated profiles: {string.Join(", ", updatedProfiles)}." : "")
                + (HasChanges ? " Click Apply to save the mod list." : "");
            if (plan.Problems.Count > 0) _dialogs.Info("Some files were skipped", string.Join("\n", plan.Problems));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ModArchiveException or InvalidDataException)
        {
            AppLog.Error("Install failed", e);
            StatusText = "Install failed.";
            _dialogs.Error("Install failed", e.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    async Task CleanUpAsync()
    {
        if (SelectedGame is not { } install || _catalog == null) return;
        var profileMods = new List<IReadOnlyList<ActiveModEntry>>();
        foreach (var p in Profiles)
        {
            try { profileMods.Add(_services.LoadProfile(p.Profile).ActiveMods); }
            catch (Exception e) when (e is SiiFormatException or IOException) { AppLog.Warn(e.Message); }
        }
        // The editor may hold unsaved enables; treat those as used too.
        profileMods.Add(ActiveMods.Select(m => m.Entry).ToList());

        var vm = new CleanupViewModel(_services, _dialogs, install, ModArchive.SuggestCleanup(_catalog, profileMods));
        _dialogs.ShowCleanup(vm);
        if (vm.Changed) await RescanAsync();
    }

    [RelayCommand(CanExecute = nameof(CanArchive))]
    async Task ArchiveModAsync(ModItemViewModel? mod)
    {
        mod ??= SelectedMod;
        if (mod?.Package is not { } package || SelectedGame is not { } install) return;
        var inUse = ActiveMods.Contains(mod);
        if (!_dialogs.Confirm("Move to archive",
                $"Move {Path.GetFileName(package.Path)} out of the mod folder into\n{install.ArchiveDir}?" +
                (inUse ? "\n\nIt is enabled in this profile and will be disabled." : "") +
                "\n\nYou can restore it later from Clean up mod folder."))
            return;
        try
        {
            if (inUse) Disable(mod);
            _services.ArchiveMod(install, package.Path, "Archived by user");
            AppLog.Info($"Archived {package.Path}");
            await RescanAsync();
        }
        catch (ModArchiveException e)
        {
            _dialogs.Error("Could not archive", e.Message);
        }
    }

    bool CanArchive(ModItemViewModel? mod) => (mod ?? SelectedMod) is { Package.IsWorkshop: false, IsMissing: false } && !IsBusy;

    partial void OnSelectedModChanged(ModItemViewModel? value) => ArchiveModCommand.NotifyCanExecuteChanged();

    // ---------------------------------------------------------------- drag & drop between and within the lists

    public void DragOver(IDropInfo dropInfo)
    {
        if (DroppedFiles(dropInfo) is { Count: > 0 } && !IsBusy)
        {
            dropInfo.DropTargetAdorner = DropTargetAdorners.Highlight;
            dropInfo.Effects = DragDropEffects.Copy;
            return;
        }
        if (!CanEdit || DraggedMods(dropInfo).Count == 0) return;
        bool toActive = ReferenceEquals(dropInfo.TargetCollection, ActiveMods);
        dropInfo.DropTargetAdorner = toActive ? DropTargetAdorners.Insert : DropTargetAdorners.Highlight;
        dropInfo.Effects = DragDropEffects.Move;
    }

    public void Drop(IDropInfo dropInfo)
    {
        if (DroppedFiles(dropInfo) is { Count: > 0 } files)
        {
            _ = InstallFilesAsync(files);
            return;
        }
        var mods = DraggedMods(dropInfo);
        if (!CanEdit || mods.Count == 0) return;

        if (ReferenceEquals(dropInfo.TargetCollection, ActiveMods))
        {
            int index = dropInfo.InsertIndex;
            foreach (var mod in mods)
            {
                if (ActiveMods.Contains(mod))
                {
                    int from = ActiveMods.IndexOf(mod);
                    int to = Math.Clamp(from < index ? index - 1 : index, 0, ActiveMods.Count - 1);
                    if (from != to) ActiveMods.Move(from, to);
                    index = to + 1;
                }
                else
                {
                    EnableAt(mod, index);
                    index = ActiveMods.IndexOf(mod) + 1;
                }
            }
            SelectedActiveMod = mods[^1];
        }
        else
        {
            foreach (var mod in mods.Where(ActiveMods.Contains).ToList()) Disable(mod);
        }
        NotifyCommands();
    }

    /// <summary>Files dragged in from Explorer.</summary>
    static IReadOnlyList<string> DroppedFiles(IDropInfo dropInfo) =>
        dropInfo.Data is IDataObject data && data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files
            ? files : [];

    static List<ModItemViewModel> DraggedMods(IDropInfo dropInfo) => dropInfo.Data switch
    {
        ModItemViewModel m => [m],
        IEnumerable e and not string => e.OfType<ModItemViewModel>().ToList(),
        _ => [],
    };
}
