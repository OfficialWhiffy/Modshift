using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TruckModsListUpdater.App.Services;
using TruckModsListUpdater.Core.Presets;

namespace TruckModsListUpdater.App.ViewModels;

/// <summary>The presets window: save the current list, apply, rename, delete, import and export presets.</summary>
public sealed partial class PresetsViewModel : ObservableObject
{
    readonly PresetStore _store;
    readonly IUserDialogs _dialogs;
    readonly string _game;
    readonly Func<string, ModPreset> _fromCurrent;
    readonly Action<ModPreset> _apply;

    public PresetsViewModel(PresetStore store, IUserDialogs dialogs, string game, string profileName,
        Func<string, ModPreset> fromCurrent, Action<ModPreset> apply)
    {
        _store = store;
        _dialogs = dialogs;
        _game = game;
        _fromCurrent = fromCurrent;
        _apply = apply;
        ProfileName = profileName;
        Reload();
    }

    public string ProfileName { get; }
    public ObservableCollection<ModPreset> Presets { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(DeleteCommand), nameof(RenameCommand), nameof(ExportCommand))]
    private ModPreset? _selectedPreset;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCurrentCommand), nameof(RenameCommand))]
    private string _newName = "";

    /// <summary>Set when a preset was applied, so the window can close.</summary>
    public bool Applied { get; private set; }
    public event Action? RequestClose;

    [RelayCommand(CanExecute = nameof(HasNewName))]
    void SaveCurrent()
    {
        var name = NewName.Trim();
        if (RejectBuiltInName(name)) return;
        if (_store.Exists(name) &&!_dialogs.Confirm("Replace preset", $"A preset called \"{name}\" already exists. Replace it?")) return;
        _store.Save(_fromCurrent(name));
        NewName = "";
        Reload(name);
    }

    bool HasNewName() => !string.IsNullOrWhiteSpace(NewName);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    void Apply()
    {
        if (SelectedPreset == null) return;
        _apply(SelectedPreset);
        Applied = true;
        RequestClose?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(CanRename))]
    void Rename()
    {
        if (SelectedPreset == null) return;
        try
        {
            var name = NewName.Trim();
            if (RejectBuiltInName(name)) return;
            _store.Rename(SelectedPreset, name);
            NewName = "";
            Reload(name);
        }
        catch (PresetFormatException e)
        {
            _dialogs.Error("Could not rename", e.Message);
        }
    }

    bool CanRename() => IsUserPresetSelected() && HasNewName();

    [RelayCommand(CanExecute = nameof(IsUserPresetSelected))]
    void Delete()
    {
        if (SelectedPreset == null || !_dialogs.Confirm("Delete preset", $"Delete the preset \"{SelectedPreset.Name}\"?")) return;
        _store.Delete(SelectedPreset.Name);
        Reload();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    void Export()
    {
        if (SelectedPreset == null || _dialogs.PickPresetExportPath(SelectedPreset.Name) is not { } path) return;
        try
        {
            PresetStore.Export(SelectedPreset, path);
        }
        catch (IOException e)
        {
            _dialogs.Error("Could not export", e.Message);
        }
    }

    [RelayCommand]
    void Import()
    {
        if (_dialogs.PickPresetFile() is not { } path) return;
        try
        {
            var preset = PresetStore.Import(path) with { Game = _game };
            // Re-importing an exported built-in must not shadow the built-in itself.
            if (BuiltInPresets.IsBuiltInName(_game, preset.Name)) preset = preset with { Name = preset.Name + " (imported)" };
            if (_store.Exists(preset.Name) && !_dialogs.Confirm("Replace preset", $"A preset called \"{preset.Name}\" already exists. Replace it?"))
                return;
            _store.Save(preset);
            Reload(preset.Name);
        }
        catch (Exception e) when (e is PresetFormatException or IOException)
        {
            _dialogs.Error("Could not import", e.Message);
        }
    }

    bool HasSelection() => SelectedPreset != null;

    bool IsUserPresetSelected() => SelectedPreset is { IsBuiltIn: false };

    bool RejectBuiltInName(string name)
    {
        if (!BuiltInPresets.IsBuiltInName(_game, name)) return false;
        _dialogs.Error("Name in use", $"\"{name}\" is a built-in preset. Pick another name.");
        return true;
    }

    void Reload(string? select = null)
    {
        var keep = select ?? SelectedPreset?.Name;
        Presets.Clear();
        foreach (var p in BuiltInPresets.List(_game).Concat(_store.List(_game))) Presets.Add(p);
        SelectedPreset = Presets.FirstOrDefault(p => p.Name == keep);
    }
}
