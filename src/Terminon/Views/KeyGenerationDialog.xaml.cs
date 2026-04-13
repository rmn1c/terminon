using System.Windows;
using System.Windows.Forms;
using Terminon.Services;
using Terminon.ViewModels;

namespace Terminon.Views;

public partial class KeyGenerationDialog : Window
{
    private readonly KeyGenerationViewModel _vm;

    public KeyGenerationDialog(KeyService keyService)
    {
        InitializeComponent();
        _vm = new KeyGenerationViewModel(keyService);
        _vm.BrowseDirectory += BrowseForDirectory;
        _vm.RequestClose += (_, _) => DialogResult = _vm.DialogResult;
        DataContext = _vm;
    }

    private string BrowseForDirectory()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Select directory to save SSH key",
            SelectedPath = _vm.SaveDirectory,
            UseDescriptionForTitle = true
        };
        return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dlg.SelectedPath : string.Empty;
    }
}
