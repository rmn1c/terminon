using Terminon.Infrastructure;
using Terminon.Services;

namespace Terminon.ViewModels;

public class KeyGenerationViewModel : ViewModelBase
{
    private readonly KeyService _keyService;
    private KeyAlgorithm _algorithm = KeyAlgorithm.Ed25519;
    private string _comment = $"{Environment.UserName}@{Environment.MachineName}";
    private string _saveDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
    private string _keyName = "id_ed25519";
    private string? _generatedPublicKey;
    private string? _fingerprint;
    private bool _isGenerating;
    private string? _statusMessage;

    public KeyAlgorithm Algorithm { get => _algorithm; set { if (SetProperty(ref _algorithm, value)) UpdateDefaultName(); } }
    public string Comment { get => _comment; set => SetProperty(ref _comment, value); }
    public string SaveDirectory { get => _saveDirectory; set => SetProperty(ref _saveDirectory, value); }
    public string KeyName { get => _keyName; set => SetProperty(ref _keyName, value); }
    public string? GeneratedPublicKey { get => _generatedPublicKey; private set => SetProperty(ref _generatedPublicKey, value); }
    public string? Fingerprint { get => _fingerprint; private set => SetProperty(ref _fingerprint, value); }
    public bool IsGenerating { get => _isGenerating; private set => SetProperty(ref _isGenerating, value); }
    public string? StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public bool HasResult => GeneratedPublicKey is not null;

    public List<KeyAlgorithm> Algorithms { get; } = Enum.GetValues<KeyAlgorithm>().ToList();

    public AsyncRelayCommand GenerateCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand CopyPublicKeyCommand { get; }
    public RelayCommand BrowseDirectoryCommand { get; }
    public RelayCommand CloseCommand { get; }

    public bool? DialogResult { get; private set; }
    public event EventHandler? RequestClose;
    public event Func<string>? BrowseDirectory;

    private GeneratedKeyPair? _lastGenerated;

    public KeyGenerationViewModel(KeyService keyService)
    {
        _keyService = keyService;
        GenerateCommand = new AsyncRelayCommand(GenerateAsync, () => !IsGenerating);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => _lastGenerated is not null && !IsGenerating);
        CopyPublicKeyCommand = new RelayCommand(
            () => { if (GeneratedPublicKey is not null) System.Windows.Clipboard.SetText(GeneratedPublicKey); },
            () => GeneratedPublicKey is not null);
        BrowseDirectoryCommand = new RelayCommand(() =>
        {
            var dir = BrowseDirectory?.Invoke();
            if (!string.IsNullOrEmpty(dir)) SaveDirectory = dir;
        });
        CloseCommand = new RelayCommand(() => { DialogResult = true; RequestClose?.Invoke(this, EventArgs.Empty); });
    }

    private async Task GenerateAsync()
    {
        IsGenerating = true;
        StatusMessage = "Generating key pair…";
        try
        {
            _lastGenerated = await _keyService.GenerateAsync(Algorithm, Comment);
            GeneratedPublicKey = _lastGenerated.PublicKey;
            Fingerprint = _lastGenerated.Fingerprint;
            StatusMessage = $"Generated {_lastGenerated.Algorithm} key pair";
            OnPropertyChanged(nameof(HasResult));
            SaveCommand.RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Generation failed: {ex.Message}";
        }
        finally { IsGenerating = false; }
    }

    private async Task SaveAsync()
    {
        if (_lastGenerated is null) return;
        Directory.CreateDirectory(SaveDirectory);
        var privatePath = Path.Combine(SaveDirectory, KeyName);
        var publicPath = privatePath + ".pub";

        try
        {
            await _keyService.SavePrivateKeyAsync(privatePath, _lastGenerated.PrivateKeyPem);
            await _keyService.SavePublicKeyAsync(publicPath, _lastGenerated.PublicKey);
            StatusMessage = $"Saved: {privatePath}";
        }
        catch (Exception ex) { StatusMessage = $"Save failed: {ex.Message}"; }
    }

    private void UpdateDefaultName()
    {
        KeyName = Algorithm switch
        {
            KeyAlgorithm.Ed25519 => "id_ed25519",
            KeyAlgorithm.RSA2048 => "id_rsa",
            KeyAlgorithm.RSA4096 => "id_rsa_4096",
            _ => "id_key"
        };
    }
}
