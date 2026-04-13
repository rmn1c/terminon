using Terminon.Infrastructure;
using Terminon.Models;
using Terminon.Services;

namespace Terminon.ViewModels;

public class ConnectionDialogViewModel : ViewModelBase
{
    private string _name = string.Empty;
    private string _host = string.Empty;
    private int _port = 22;
    private string _username = string.Empty;
    private AuthMethod _authMethod = AuthMethod.Password;
    private string _password = string.Empty;
    private string _privateKeyPath = string.Empty;
    private string _passphrase = string.Empty;
    private string _folderId = string.Empty;
    private string _notes = string.Empty;
    private bool _saveProfile;
    private string? _errorMessage;

    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string Host { get => _host; set => SetProperty(ref _host, value); }
    public int Port { get => _port; set => SetProperty(ref _port, value); }
    public string Username { get => _username; set => SetProperty(ref _username, value); }
    public AuthMethod AuthMethod { get => _authMethod; set { if (SetProperty(ref _authMethod, value)) OnPropertyChanged(nameof(IsPasswordAuth)); } }
    public string Password { get => _password; set => SetProperty(ref _password, value); }
    public string PrivateKeyPath { get => _privateKeyPath; set => SetProperty(ref _privateKeyPath, value); }
    public string Passphrase { get => _passphrase; set => SetProperty(ref _passphrase, value); }
    public string FolderId { get => _folderId; set => SetProperty(ref _folderId, value); }
    public string Notes { get => _notes; set => SetProperty(ref _notes, value); }
    public bool SaveProfile { get => _saveProfile; set => SetProperty(ref _saveProfile, value); }
    public string? ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }

    public bool IsPasswordAuth => AuthMethod == AuthMethod.Password;

    public List<AuthMethod> AuthMethods { get; } = Enum.GetValues<AuthMethod>().ToList();
    public List<ConnectionFolder> Folders { get; set; } = new();

    public RelayCommand BrowseKeyFileCommand { get; }
    public RelayCommand ConnectCommand { get; }
    public RelayCommand CancelCommand { get; }

    public bool? DialogResult { get; private set; }
    public event EventHandler? RequestClose;
    public event Func<string>? BrowseForKeyFile;

    public ConnectionDialogViewModel()
    {
        BrowseKeyFileCommand = new RelayCommand(() =>
        {
            var path = BrowseForKeyFile?.Invoke();
            if (!string.IsNullOrEmpty(path)) PrivateKeyPath = path;
        });

        ConnectCommand = new RelayCommand(Connect, CanConnect);
        CancelCommand = new RelayCommand(Cancel);
    }

    public void LoadProfile(ConnectionProfile profile)
    {
        Name = profile.Name;
        Host = profile.Host;
        Port = profile.Port;
        Username = profile.Username;
        AuthMethod = profile.AuthMethod;
        PrivateKeyPath = profile.PrivateKeyPath ?? string.Empty;
        FolderId = profile.FolderId;
        Notes = profile.Notes ?? string.Empty;
        // Don't load password — user must re-enter
    }

    public ConnectionProfile BuildProfile(string? existingId = null)
    {
        var profile = new ConnectionProfile
        {
            Id = existingId ?? Guid.NewGuid().ToString(),
            Name = string.IsNullOrEmpty(Name) ? $"{Username}@{Host}" : Name,
            Host = Host,
            Port = Port,
            Username = Username,
            AuthMethod = AuthMethod,
            FolderId = FolderId,
            Notes = Notes,
        };

        if (AuthMethod == AuthMethod.Password && !string.IsNullOrEmpty(Password))
            profile.EncryptedPassword = ProfileService.EncryptPassword(Password);

        if (AuthMethod == AuthMethod.PrivateKey)
        {
            profile.PrivateKeyPath = PrivateKeyPath;
            if (!string.IsNullOrEmpty(Passphrase))
                profile.EncryptedPassphrase = ProfileService.EncryptPassword(Passphrase);
        }

        return profile;
    }

    private bool CanConnect()
        => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Username) && Port is > 0 and <= 65535;

    private void Connect()
    {
        if (!CanConnect()) return;
        ErrorMessage = null;
        DialogResult = true;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    private void Cancel()
    {
        DialogResult = false;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}
