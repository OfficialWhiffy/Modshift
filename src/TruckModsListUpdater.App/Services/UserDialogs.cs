using System.Diagnostics;
using System.Windows;

namespace TruckModsListUpdater.App.Services;

public enum SaveChoice { Save, Discard, Cancel }

/// <summary>Everything the view models ask the user, so they can be tested without windows.</summary>
public interface IUserDialogs
{
    bool Confirm(string title, string message);
    SaveChoice AskSaveChanges(string message);
    void Error(string title, string message);
    void Info(string title, string message);
    void OpenFolder(string path);
    IReadOnlyList<string>? PickModFiles();
    bool ShowInstall(ViewModels.InstallViewModel vm);
    void ShowCleanup(ViewModels.CleanupViewModel vm);
    void ShowPresets(ViewModels.PresetsViewModel vm);
    string? PickPresetFile();
    string? PickPresetExportPath(string suggestedName);
}

public sealed class UserDialogs : IUserDialogs
{
    static Window? Owner => Application.Current?.MainWindow;

    public bool Confirm(string title, string message) =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public SaveChoice AskSaveChanges(string message) =>
        Show(message, "Unsaved changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
        {
            MessageBoxResult.Yes => SaveChoice.Save,
            MessageBoxResult.No => SaveChoice.Discard,
            _ => SaveChoice.Cancel,
        };

    public void Error(string title, string message) => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void Info(string title, string message) => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    /// <summary>Opens Explorer at a folder, or with a file selected.</summary>
    public void OpenFolder(string path)
    {
        var args = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true })?.Dispose();
    }

    public IReadOnlyList<string>? PickModFiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Install mods",
            Filter = "Mods and downloads (*.scs;*.zip)|*.scs;*.zip|All files (*.*)|*.*",
            Multiselect = true,
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileNames : null;
    }

    public bool ShowInstall(ViewModels.InstallViewModel vm) =>
        new Views.InstallWindow { DataContext = vm, Owner = Owner }.ShowDialog() == true;

    public void ShowCleanup(ViewModels.CleanupViewModel vm) =>
        new Views.CleanupWindow { DataContext = vm, Owner = Owner }.ShowDialog();

    public void ShowPresets(ViewModels.PresetsViewModel vm) =>
        new Views.PresetsWindow { DataContext = vm, Owner = Owner }.ShowDialog();

    public string? PickPresetFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Import preset", Filter = "Mod presets (*.json)|*.json" };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? PickPresetExportPath(string suggestedName)
    {
        var safe = string.Concat(suggestedName.Split(Path.GetInvalidFileNameChars()));
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Export preset", Filter = "Mod presets (*.json)|*.json", FileName = safe + ".json" };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image) =>
        Owner is { IsVisible: true } owner
            ? MessageBox.Show(owner, message, title, buttons, image)
            : MessageBox.Show(message, title, buttons, image);
}
