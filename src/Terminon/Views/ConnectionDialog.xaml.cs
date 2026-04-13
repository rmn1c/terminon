using Microsoft.Win32;
using System.Windows;
using Terminon.Models;
using Terminon.Services;
using Terminon.ViewModels;

namespace Terminon.Views;

public partial class ConnectionDialog : Window
{
    private readonly ConnectionDialogViewModel _vm;
    private readonly ProfileService _profileService;

    public ConnectionProfile? ResultProfile { get; private set; }

    public ConnectionDialog(ConnectionProfile? prefill, ProfileService profileService)
    {
        _profileService = profileService;
        InitializeComponent();

        _vm = new ConnectionDialogViewModel();
        _vm.Folders = profileService.Folders.ToList();
        _vm.BrowseForKeyFile += BrowseKeyFile;
        _vm.RequestClose += (_, _) => DialogResult = _vm.DialogResult;

        if (prefill is not null) _vm.LoadProfile(prefill);
        DataContext = _vm;

        Loaded += (_, _) => HostBox.Focus();
    }

    private string BrowseKeyFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select Private Key File",
            Filter = "All files (*.*)|*.*|PEM files (*.pem)|*.pem|OpenSSH keys|id_*",
            InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh")
        };
        return dlg.ShowDialog() == true ? dlg.FileName : string.Empty;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (_vm.DialogResult == true)
        {
            // Capture password from PasswordBox (SecureString → plain for SSH.NET)
            if (_vm.IsPasswordAuth)
                _vm.Password = new System.Net.NetworkCredential(string.Empty, PasswordBox.SecurePassword).Password;
            else
                _vm.Passphrase = new System.Net.NetworkCredential(string.Empty, PassphraseBox.SecurePassword).Password;

            ResultProfile = _vm.BuildProfile();

            if (_vm.SaveProfile)
                _profileService.AddProfile(ResultProfile);
        }
    }
}
