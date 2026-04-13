using System.Collections.ObjectModel;
using System.Windows;
using Terminon.Infrastructure;
using Terminon.Models;
using Terminon.Services;

namespace Terminon.ViewModels;

public class ConnectionTreeItem : ViewModelBase
{
    private bool _isExpanded = true;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public ConnectionProfile? Profile { get; set; }
    public ObservableCollection<ConnectionTreeItem> Children { get; } = new();
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }
}

public class MainViewModel : ViewModelBase
{
    private readonly SshConnectionService _sshService;
    private readonly ProfileService _profileService;
    private readonly SettingsService _settingsService;
    private readonly SftpService _sftpService;
    private readonly KeyService _keyService;

    private SessionTabViewModel? _activeTab;
    private string _quickConnectText = string.Empty;
    private bool _isSidebarVisible = true;
    private string _appTitle = "Terminon";

    public ObservableCollection<SessionTabViewModel> Tabs { get; } = new();
    public ObservableCollection<ConnectionTreeItem> ConnectionTree { get; } = new();
    public ObservableCollection<ConnectionProfile> RecentConnections { get; } = new();

    public SessionTabViewModel? ActiveTab
    {
        get => _activeTab;
        set
        {
            if (SetProperty(ref _activeTab, value))
                OnPropertyChanged(nameof(HasActiveTab));
        }
    }

    public string QuickConnectText { get => _quickConnectText; set => SetProperty(ref _quickConnectText, value); }
    public bool IsSidebarVisible { get => _isSidebarVisible; set => SetProperty(ref _isSidebarVisible, value); }
    public string AppTitle { get => _appTitle; set => SetProperty(ref _appTitle, value); }
    public bool HasActiveTab => _activeTab is not null;

    private ConnectionProfile? _selectedRecent;
    public ConnectionProfile? SelectedRecent
    {
        get => _selectedRecent;
        set
        {
            if (SetProperty(ref _selectedRecent, value) && value is not null)
                _ = ConnectProfileAsync(value);
        }
    }

    public AsyncRelayCommand NewTabCommand { get; }
    public AsyncRelayCommand<ConnectionProfile> ConnectProfileCommand { get; }
    public AsyncRelayCommand QuickConnectCommand { get; }
    public RelayCommand CloseTabCommand { get; }
    public RelayCommand ToggleSidebarCommand { get; }
    public AsyncRelayCommand<SessionTabViewModel> CloseSpecificTabCommand { get; }
    public AsyncRelayCommand OpenConnectionDialogCommand { get; }
    public AsyncRelayCommand OpenSettingsCommand { get; }
    public AsyncRelayCommand OpenKeygenCommand { get; }
    public RelayCommand NextTabCommand { get; }
    public RelayCommand PrevTabCommand { get; }

    // Events to communicate with the View for dialogs
    public event Func<ConnectionProfile?, Task<ConnectionProfile?>>? ShowConnectionDialog;
    public event Func<Task>? ShowSettingsDialog;
    public event Func<Task>? ShowKeygenDialog;
    public event Func<string, string, string, Task<bool>>? ShowHostKeyDialog;
    public event Func<string, string, Task<bool>>? ShowReconnectDialog;

    public MainViewModel(
        SshConnectionService sshService,
        ProfileService profileService,
        SettingsService settingsService,
        SftpService sftpService,
        KeyService keyService)
    {
        _sshService = sshService;
        _profileService = profileService;
        _settingsService = settingsService;
        _sftpService = sftpService;
        _keyService = keyService;

        NewTabCommand = new AsyncRelayCommand(NewTabAsync);
        ConnectProfileCommand = new AsyncRelayCommand<ConnectionProfile>(p => ConnectProfileAsync(p!));
        QuickConnectCommand = new AsyncRelayCommand(QuickConnectAsync, () => !string.IsNullOrWhiteSpace(QuickConnectText));
        CloseTabCommand = new RelayCommand(CloseActiveTab, () => HasActiveTab);
        ToggleSidebarCommand = new RelayCommand(() => IsSidebarVisible = !IsSidebarVisible);
        CloseSpecificTabCommand = new AsyncRelayCommand<SessionTabViewModel>(t => { CloseTab(t!); return Task.CompletedTask; });
        OpenConnectionDialogCommand = new AsyncRelayCommand(OpenConnectionDialogAsync);
        OpenSettingsCommand = new AsyncRelayCommand(async () => { if (ShowSettingsDialog is not null) await ShowSettingsDialog(); });
        OpenKeygenCommand = new AsyncRelayCommand(async () => { if (ShowKeygenDialog is not null) await ShowKeygenDialog(); });
        NextTabCommand = new RelayCommand(SwitchNextTab, () => Tabs.Count > 1);
        PrevTabCommand = new RelayCommand(SwitchPrevTab, () => Tabs.Count > 1);
    }

    public void Initialize()
    {
        _profileService.Load();
        _settingsService.Load();
        RefreshConnectionTree();
        RefreshRecentConnections();
    }

    public void RefreshConnectionTree()
    {
        ConnectionTree.Clear();

        // Build folder nodes
        var folderNodes = new Dictionary<string, ConnectionTreeItem>();
        foreach (var folder in _profileService.Folders)
        {
            var node = new ConnectionTreeItem { Id = folder.Id, Name = folder.Name, IsFolder = true, IsExpanded = folder.IsExpanded };
            folderNodes[folder.Id] = node;
        }

        // Add profiles to folders
        foreach (var profile in _profileService.Profiles.OrderBy(p => p.Name))
        {
            var item = new ConnectionTreeItem { Id = profile.Id, Name = profile.Name, IsFolder = false, Profile = profile };
            if (folderNodes.TryGetValue(profile.FolderId, out var folder))
                folder.Children.Add(item);
            else
                ConnectionTree.Add(item);
        }

        // Add non-empty folders
        foreach (var (_, node) in folderNodes)
            if (node.Children.Count > 0)
                ConnectionTree.Add(node);
    }

    public void RefreshRecentConnections()
    {
        RecentConnections.Clear();
        foreach (var p in _profileService.GetRecent())
            RecentConnections.Add(p);
    }

    private async Task NewTabAsync()
    {
        var profile = await ShowConnectionDialog?.Invoke(null) ?? null;
        if (profile is null) return;
        await ConnectProfileAsync(profile);
    }

    public async Task ConnectProfileAsync(ConnectionProfile profile)
    {
        var sftpPanel = new SftpPanelViewModel(_sftpService);
        var tab = new SessionTabViewModel(_sshService, sftpPanel);
        tab.TerminalTitleChanged += t => { if (ActiveTab == tab) AppTitle = $"Terminon — {t}"; };

        Tabs.Add(tab);
        ActiveTab = tab;

        try
        {
            await tab.ConnectAsync(profile, 80, 24,
                async (fp, kt, reason) => await OnHostKeyVerification(fp, kt, reason));

            profile.LastConnected = DateTime.UtcNow;
            _profileService.UpdateProfile(profile);
            RefreshRecentConnections();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Connection failed: {ex.Message}", "Connection Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            CloseTab(tab);
        }
    }

    private async Task<bool> OnHostKeyVerification(string fingerprint, string keyType, string reason)
    {
        if (ShowHostKeyDialog is not null)
            return await ShowHostKeyDialog(fingerprint, keyType, reason);
        return false;
    }

    private async Task QuickConnectAsync()
    {
        var text = QuickConnectText.Trim();
        if (string.IsNullOrEmpty(text)) return;

        // Parse: [user@]host[:port]
        string username = Environment.UserName;
        string host = text;
        int port = 22;

        if (text.Contains('@'))
        {
            var parts = text.Split('@', 2);
            username = parts[0];
            host = parts[1];
        }
        if (host.Contains(':'))
        {
            var parts = host.Split(':', 2);
            host = parts[0];
            int.TryParse(parts[1], out port);
        }

        var profile = new ConnectionProfile
        {
            Name = $"{username}@{host}",
            Host = host,
            Port = port,
            Username = username,
            AuthMethod = AuthMethod.Password,
        };

        // Ask for password via connection dialog
        var filled = await ShowConnectionDialog?.Invoke(profile) ?? null;
        if (filled is null) return;

        QuickConnectText = string.Empty;
        await ConnectProfileAsync(filled);
    }

    private async Task OpenConnectionDialogAsync()
    {
        var profile = await ShowConnectionDialog?.Invoke(null) ?? null;
        if (profile is null) return;
        await ConnectProfileAsync(profile);
    }

    private void CloseActiveTab()
    {
        if (ActiveTab is not null) CloseTab(ActiveTab);
    }

    private void CloseTab(SessionTabViewModel tab)
    {
        int idx = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        _ = tab.DisposeAsync();

        if (Tabs.Count == 0)
            ActiveTab = null;
        else
            ActiveTab = Tabs[Math.Min(idx, Tabs.Count - 1)];
    }

    private void SwitchNextTab()
    {
        if (Tabs.Count == 0) return;
        int idx = ActiveTab is not null ? Tabs.IndexOf(ActiveTab) : -1;
        ActiveTab = Tabs[(idx + 1) % Tabs.Count];
    }

    private void SwitchPrevTab()
    {
        if (Tabs.Count == 0) return;
        int idx = ActiveTab is not null ? Tabs.IndexOf(ActiveTab) : 0;
        ActiveTab = Tabs[(idx - 1 + Tabs.Count) % Tabs.Count];
    }
}
