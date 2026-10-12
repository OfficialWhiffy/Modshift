using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using TruckModsListUpdater.App.Services;
using TruckModsListUpdater.App.ViewModels;

namespace TruckModsListUpdater.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    MainViewModel Vm => (MainViewModel)DataContext;

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm && !vm.ConfirmClose()) e.Cancel = true;
    }

    void OnAvailableDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemUnderMouse(e) is { } mod) Vm.EnableCommand.Execute(mod);
    }

    void OnActiveDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemUnderMouse(e) is { } mod) Vm.DisableCommand.Execute(mod);
    }

    void OnActiveKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var command = key switch
        {
            Key.Delete => Vm.DisableCommand as ICommand,
            Key.Up when alt => Vm.MoveUpCommand,
            Key.Down when alt => Vm.MoveDownCommand,
            Key.Home when alt => Vm.MoveToTopCommand,
            Key.End when alt => Vm.MoveToBottomCommand,
            _ => null,
        };
        if (command?.CanExecute(null) != true) return;
        command.Execute(null);
        e.Handled = true;
        // Keep keyboard focus on the moved item.
        if (ActiveList.SelectedItem is { } item && ActiveList.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container)
            container.Focus();
    }

    /// <summary>Files dropped outside the two lists (the lists handle their own drops).</summary>
    void OnWindowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && !Vm.IsBusy ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    async void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            e.Handled = true;
            await Vm.InstallFilesAsync(files);
        }
    }

    void OnRestoreMenuOpened(object sender, RoutedEventArgs e) => Vm.RefreshBackups();

    void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        ExternalLinks.TryOpen(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    static ModItemViewModel? ItemUnderMouse(MouseButtonEventArgs e) =>
        (e.OriginalSource as FrameworkElement)?.DataContext as ModItemViewModel;
}
