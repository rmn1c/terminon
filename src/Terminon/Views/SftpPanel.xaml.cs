using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Terminon.Models;
using Terminon.ViewModels;

namespace Terminon.Views;

public partial class SftpPanel : UserControl
{
    public SftpPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is SftpPanelViewModel vm)
        {
            vm.RequestSaveDialog += RequestSaveDialogAsync;
            vm.RequestOpenDialog += RequestOpenDialogAsync;
            vm.RequestInputDialog += RequestInputDialogAsync;
        }
    }

    private async Task<string?> RequestSaveDialogAsync(string fileName)
    {
        var dlg = new SaveFileDialog { FileName = fileName };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private async Task<string[]?> RequestOpenDialogAsync(string[] filters)
    {
        var dlg = new OpenFileDialog { Multiselect = true };
        return dlg.ShowDialog() == true ? dlg.FileNames : null;
    }

    private async Task<string?> RequestInputDialogAsync(string prompt)
    {
        var dlg = new InputDialog(prompt);
        dlg.Owner = Window.GetWindow(this);
        return dlg.ShowDialog() == true ? dlg.InputText : null;
    }

    private void FileList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is SftpPanelViewModel vm && vm.SelectedEntry is SftpEntry entry)
            vm.OpenEntryCommand.Execute(entry);
    }
}
