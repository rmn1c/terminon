using System.Windows;
using System.Windows.Threading;
using Terminon.Infrastructure;
using Terminon.Models;
using Terminon.Services;
using Terminon.Terminal;

namespace Terminon.ViewModels;

public class SessionTabViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly SshConnectionService _sshService;
    private SshSession? _session;
    private readonly TerminalBuffer _buffer;
    private readonly VT100Parser _parser;
    private readonly DispatcherTimer _uptimeTimer;
    private readonly CancellationTokenSource _cts = new();

    private string _title = "New Tab";
    private string _statusText = "Disconnected";
    private SessionState _state = SessionState.Disconnected;
    private long _latencyMs;
    private TimeSpan _uptime;
    private bool _isSearchVisible;
    private string _searchText = string.Empty;
    private bool _isSftpVisible;

    public string Id { get; } = Guid.NewGuid().ToString();
    public ConnectionProfile? Profile { get; private set; }
    public TerminalBuffer Buffer => _buffer;
    public SshSession? Session => _session;
    public SftpPanelViewModel SftpPanel { get; }

    public string Title { get => _title; private set => SetProperty(ref _title, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public SessionState State { get => _state; private set { if (SetProperty(ref _state, value)) { OnPropertyChanged(nameof(IsConnected)); OnPropertyChanged(nameof(IsConnecting)); } } }
    public long LatencyMs { get => _latencyMs; private set => SetProperty(ref _latencyMs, value); }
    public TimeSpan Uptime { get => _uptime; private set => SetProperty(ref _uptime, value); }
    public bool IsSearchVisible { get => _isSearchVisible; set => SetProperty(ref _isSearchVisible, value); }
    public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value)) PerformSearch(); } }
    public bool IsSftpVisible { get => _isSftpVisible; set => SetProperty(ref _isSftpVisible, value); }

    public bool IsConnected => _state == SessionState.Connected;
    public bool IsConnecting => _state == SessionState.Connecting || _state == SessionState.Reconnecting;

    public RelayCommand DisconnectCommand { get; }
    public RelayCommand ToggleSearchCommand { get; }
    public RelayCommand ToggleSftpCommand { get; }
    public RelayCommand ClearScrollbackCommand { get; }
    public AsyncRelayCommand PingCommand { get; }

    public event EventHandler? CloseRequested;
    public event Action<string>? TerminalTitleChanged;

    public SessionTabViewModel(SshConnectionService sshService, SftpPanelViewModel sftpPanel)
    {
        _sshService = sshService;
        SftpPanel = sftpPanel;

        _buffer = new TerminalBuffer(80, 24);
        _parser = new VT100Parser(_buffer);
        _buffer.TitleChanged += t => { Title = t; TerminalTitleChanged?.Invoke(t); };
        _buffer.BellTriggered += () => Application.Current.Dispatcher.InvokeAsync(HandleBell);

        _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uptimeTimer.Tick += (_, _) =>
        {
            if (_session is not null && _state == SessionState.Connected)
                Uptime = DateTime.UtcNow - _session.ConnectedAt;
        };

        DisconnectCommand = new RelayCommand(async () => await DisconnectAsync(), () => IsConnected || IsConnecting);
        ToggleSearchCommand = new RelayCommand(() => { IsSearchVisible = !IsSearchVisible; if (!IsSearchVisible) SearchText = string.Empty; });
        ToggleSftpCommand = new RelayCommand(() => IsSftpVisible = !IsSftpVisible, () => IsConnected);
        ClearScrollbackCommand = new RelayCommand(() => _buffer.EraseInDisplay(3));
        PingCommand = new AsyncRelayCommand(MeasureLatencyAsync, () => IsConnected);
    }

    public async Task ConnectAsync(
        ConnectionProfile profile,
        int cols, int rows,
        Func<string, string, string, Task<bool>> hostKeyCallback,
        CancellationToken ct = default)
    {
        Profile = profile;
        Title = profile.Name.Length > 0 ? profile.Name : $"{profile.Username}@{profile.Host}";
        StatusText = "Connecting…";
        State = SessionState.Connecting;

        try
        {
            _session = await _sshService.ConnectAsync(profile, cols, rows, hostKeyCallback, ct);
            _session.DataReceived += OnDataReceived;
            _session.StateChanged += OnStateChanged;

            State = SessionState.Connected;
            StatusText = $"{profile.Username}@{profile.Host}:{profile.Port}";
            _uptimeTimer.Start();

            // Connect SFTP if needed
            _ = Task.Run(async () =>
            {
                try { await SftpPanel.ConnectAsync(profile, ct); }
                catch (Exception ex) { Serilog.Log.Warning(ex, "SFTP auto-connect failed"); }
            }, ct);
        }
        catch (Exception ex)
        {
            State = SessionState.Failed;
            StatusText = $"Failed: {ex.Message}";
            Serilog.Log.Error(ex, "SSH connect failed");
            throw;
        }
    }

    public async Task ReconnectAsync(CancellationToken ct = default)
    {
        if (Profile is null) return;
        if (_session is not null) await DisposeSessionAsync();

        State = SessionState.Reconnecting;
        StatusText = "Reconnecting…";
        _buffer.EraseInDisplay(2);
        _parser.Feed(System.Text.Encoding.UTF8.GetBytes("\r\n\x1b[33m[Reconnecting...]\x1b[0m\r\n"));

        await ConnectAsync(Profile, _buffer.Columns, _buffer.Rows,
            async (fp, kt, reason) => { /* accept on reconnect */ return true; }, ct);
    }

    private void OnDataReceived(object? sender, byte[] data)
        => Application.Current.Dispatcher.InvokeAsync(() =>
        {
            _parser.Feed(data);
        }, System.Windows.Threading.DispatcherPriority.Render);

    private void OnStateChanged(object? sender, SessionStateChangedArgs e)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            State = e.NewState;
            if (e.NewState == SessionState.Disconnected)
            {
                StatusText = e.Message ?? "Disconnected";
                _uptimeTimer.Stop();
                _parser.Feed(System.Text.Encoding.UTF8.GetBytes($"\r\n\x1b[31m[{StatusText}]\x1b[0m\r\n"));
            }
        });
    }

    public async Task SendDataAsync(byte[] data)
    {
        if (_session is null || !IsConnected) return;
        try { await _session.WriteAsync(data, _cts.Token); }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Failed to write to SSH session"); }
    }

    public async Task ResizeTerminalAsync(int cols, int rows)
    {
        _buffer.Resize(cols, rows);
        if (_session is not null && IsConnected)
            await _session.ResizeAsync(cols, rows);
    }

    private async Task DisconnectAsync()
    {
        _cts.Cancel();
        if (_session is not null) await DisposeSessionAsync();
        State = SessionState.Disconnected;
        StatusText = "Disconnected";
        _uptimeTimer.Stop();
    }

    private async Task DisposeSessionAsync()
    {
        if (_session is null) return;
        _session.DataReceived -= OnDataReceived;
        _session.StateChanged -= OnStateChanged;
        await _session.DisposeAsync();
        _session = null;
    }

    private async Task MeasureLatencyAsync()
    {
        if (_session is null) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await _session.ExecuteCommandAsync("echo ping", _cts.Token);
            sw.Stop();
            LatencyMs = sw.ElapsedMilliseconds;
        }
        catch { }
    }

    private void PerformSearch()
    {
        // Buffer search is handled in the TerminalControl directly
        // Signal the control via an event
    }

    private void HandleBell()
    {
        // Visual bell — the TerminalControl handles the flash
        // System bell
        SystemSounds?.Invoke();
    }

    public event Action? SystemSounds;

    public async ValueTask DisposeAsync()
    {
        _uptimeTimer.Stop();
        if (!_cts.IsCancellationRequested)
            _cts.Cancel();
        await DisposeSessionAsync();
        await SftpPanel.DisposeAsync();
        try { _cts.Dispose(); } catch { }
    }
}
