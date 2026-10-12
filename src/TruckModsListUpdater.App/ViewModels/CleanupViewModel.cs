using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TruckModsListUpdater.App.Services;
using TruckModsListUpdater.Core.Games;
using TruckModsListUpdater.Core.Mods;
using TruckModsListUpdater.Core.Storage;

namespace TruckModsListUpdater.App.ViewModels;

/// <summary>"Clean up mod folder": archive suggestions, and the archive itself with restore.</summary>
public sealed partial class CleanupViewModel : ObservableObject
{
    readonly IGameServices _services;
    readonly IUserDialogs _dialogs;
    readonly GameInstallation _install;

    public CleanupViewModel(IGameServices services, IUserDialogs dialogs, GameInstallation install, IReadOnlyList<CleanupSuggestion> suggestions)
    {
        _services = services;
        _dialogs = dialogs;
        _install = install;
        foreach (var s in suggestions) Suggestions.Add(new SuggestionItem(s));
        ReloadArchive();
    }

    public ObservableCollection<SuggestionItem> Suggestions { get; } = [];
    public ObservableCollection<ArchivedMod> Archived { get; } = [];
    public string ArchiveDir => _install.ArchiveDir;

    /// <summary>True once anything was moved, so the main window rescans.</summary>
    public bool Changed { get; private set; }

    [RelayCommand]
    void ArchiveSelected()
    {
        var selected = Suggestions.Where(s => s.IsSelected).ToList();
        if (selected.Count == 0) return;
        var failures = new List<string>();
        foreach (var s in selected)
        {
            try
            {
                _services.ArchiveMod(_install, s.Suggestion.Package.Path, s.ReasonText);
                Suggestions.Remove(s);
                Changed = true;
                AppLog.Info($"Archived {s.Suggestion.Package.Path} ({s.ReasonText})");
            }
            catch (ModArchiveException e)
            {
                failures.Add(e.Message);
            }
        }
        ReloadArchive();
        if (failures.Count > 0) _dialogs.Error("Some mods could not be archived", string.Join("\n", failures));
    }

    [RelayCommand]
    void Restore(ArchivedMod? item)
    {
        if (item == null) return;
        try
        {
            _services.RestoreArchivedMod(_install, item);
            Changed = true;
            AppLog.Info($"Restored {item.ArchivedPath} to {item.OriginalPath}");
        }
        catch (ModArchiveException e)
        {
            _dialogs.Error("Could not restore", e.Message);
        }
        ReloadArchive();
    }

    [RelayCommand]
    void OpenArchiveFolder()
    {
        Directory.CreateDirectory(_install.ArchiveDir);
        _dialogs.OpenFolder(_install.ArchiveDir);
    }

    void ReloadArchive()
    {
        Archived.Clear();
        foreach (var a in _services.ArchivedMods(_install)) Archived.Add(a);
    }

    public sealed partial class SuggestionItem(CleanupSuggestion suggestion) : ObservableObject
    {
        public CleanupSuggestion Suggestion { get; } = suggestion;
        public string Name => Path.GetFileName(Suggestion.Package.Path);
        public string DisplayName => Suggestion.Package.DisplayName;
        public string Explanation => Suggestion.Explanation;
        public string ReasonText => Suggestion.Reason switch
        {
            CleanupReason.BrokenFolder => "Broken folder",
            CleanupReason.OlderVersion => "Older version",
            _ => "Unused",
        };

        [ObservableProperty] private bool _isSelected = suggestion.Recommended;
    }
}
