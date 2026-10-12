using System.Windows;
using System.Windows.Threading;
using TruckModsListUpdater.App.Services;
using TruckModsListUpdater.App.ViewModels;
using TruckModsListUpdater.App.Views;
using TruckModsListUpdater.Core;
using TruckModsListUpdater.Core.Platform;
using TruckModsListUpdater.Core.Storage;

namespace TruckModsListUpdater.App;

public partial class App : Application
{
    Mutex? _singleInstance;
    GameProcessMonitor? _monitor;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Two instances editing the same profile would overwrite each other.
        _singleInstance = new Mutex(true, "Modshift.SingleInstance", out bool first);
        if (!first)
        {
            MessageBox.Show("Modshift is already running.", "Modshift", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        AppPaths.MigrateLegacyFolders(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var paths = AppPaths.Default;
        AppLog.Initialize(paths.LogsDir);
        var version = AppVersion.FromAssembly(typeof(App).Assembly);
        AppLog.Info($"Starting Modshift {version.Detailed}");
        DispatcherUnhandledException += OnUnhandledException;

        var settings = AppSettings.Load(paths.SettingsFile);
        _monitor = new GameProcessMonitor();
        var services = new GameServices(paths, settings, _monitor);
        var vm = new MainViewModel(services, new UserDialogs(), settings, () => SaveSettings(settings, paths));
        _monitor.RunningChanged += (game, running) => Dispatcher.BeginInvoke(() => vm.OnGameRunningChanged(game, running));

        var window = new MainWindow { DataContext = vm, Title = $"Modshift {version.Display}" };
        MainWindow = window;
        window.Show();
        await vm.InitializeAsync();
    }

    static void SaveSettings(AppSettings settings, AppPaths paths)
    {
        try { settings.Save(paths.SettingsFile); }
        catch (IOException ex) { AppLog.Warn("Could not save settings: " + ex.Message); }
    }

    void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("Unhandled exception", e.Exception);
        MessageBox.Show($"Something went wrong:\n\n{e.Exception.Message}\n\nDetails were written to the log in %LOCALAPPDATA%\\Modshift\\logs.",
            "Modshift", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _monitor?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
