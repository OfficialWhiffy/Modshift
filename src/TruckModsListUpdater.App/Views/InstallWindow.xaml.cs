using System.Windows;

namespace TruckModsListUpdater.App.Views;

public partial class InstallWindow : Window
{
    public InstallWindow() => InitializeComponent();

    void OnInstall(object sender, RoutedEventArgs e) => DialogResult = true;
}
