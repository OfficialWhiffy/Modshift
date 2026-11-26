using System.Windows;
using System.Windows.Threading;
using TruckModsListUpdater.Core;
using TruckModsListUpdater.Core.Platform;
using TruckModsListUpdater.Core.Storage;
using TruckModsListUpdater.ProfileCopy.Services;
using TruckModsListUpdater.ProfileCopy.ViewModels;
using TruckModsListUpdater.ProfileCopy.Views;

namespace TruckModsListUpdater.ProfileCopy;

public partial class App : Application
{
    Mutex? _singleInstance;
    GameProcessMonitor? _monitor;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Two instances copying into the same profile would overwrite each other.
        _singleInstance = new Mutex(true, "Modshift.ProfileCopy.SingleInstance", out bool first);
        if (!first)
        {
            MessageBox.Show("Modshift Profile Copy is already running.", "Modshift Profile Copy", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Shares Modshift's log folder; its backups go to their own config-backups folder.
        var paths = AppPaths.Default;
        AppLog.Initialize(paths.LogsDir);
        var version = AppVersion.FromAssembly(typeof(App).Assembly);
        AppLog.Info($"Starting Modshift Profile Copy {version.Detailed}");
        DispatcherUnhandledException += OnUnhandledException;

        _monitor = new GameProcessMonitor();
        var vm = new MainViewModel(new CopyServices(paths, _monitor), new Dialogs());
        _monitor.RunningChanged += (game, running) => Dispatcher.BeginInvoke(() => vm.OnGameRunningChanged(game, running));

        var window = new MainWindow { DataContext = vm, Title = $"Modshift Profile Copy {version.Display}" };
        MainWindow = window;
        window.Show();
        await vm.InitializeAsync();
    }

    void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("Unhandled exception", e.Exception);
        MessageBox.Show($"Something went wrong:\n\n{e.Exception.Message}\n\nDetails were written to the log in %LOCALAPPDATA%\\Modshift\\logs.",
            "Modshift Profile Copy", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _monitor?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
