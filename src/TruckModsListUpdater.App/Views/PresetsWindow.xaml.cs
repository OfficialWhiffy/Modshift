using System.Windows;
using System.Windows.Input;
using TruckModsListUpdater.App.ViewModels;

namespace TruckModsListUpdater.App.Views;

public partial class PresetsWindow : Window
{
    public PresetsWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is PresetsViewModel vm) vm.RequestClose += Close;
        };
    }

    void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PresetsViewModel vm && vm.ApplyCommand.CanExecute(null)) vm.ApplyCommand.Execute(null);
    }
}
