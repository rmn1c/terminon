using System.Windows;
using Terminon.Services;
using Terminon.ViewModels;

namespace Terminon.Views;

public partial class SettingsDialog : Window
{
    public SettingsDialog(SettingsService settingsService)
    {
        InitializeComponent();
        var vm = new SettingsViewModel(settingsService);
        vm.RequestClose += (_, _) => DialogResult = vm.DialogResult;
        DataContext = vm;
    }
}
