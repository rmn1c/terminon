using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;
using Terminon.Models;

namespace Terminon.Services;

public enum SessionState { Disconnected, Connecting, Connected, Reconnecting, Failed }

public class SessionStateChangedArgs(SessionState old, SessionState current, string? message = null) : EventArgs
{
    public SessionState OldState { get; } = old;
    public SessionState NewState { get; } = current;
    public string? Message { get; } = message;
}

public class SshSession : IAsyncDisposable
{
    private readonly SshClient _client;
    private ShellStream? _shell;
    private readonly CancellationTokenSource _cts = new();
    private Task? _readTask;

    public string Id { get; } = Guid.NewGuid().ToString();
    public ConnectionProfile Profile { get; }
    public SessionState State { get; private set; } = SessionState.Disconnected;
    public DateTime ConnectedAt { get; private set; }
    public long BytesReceived { get; private set; }
    public long BytesSent { get; private set; }

    public event EventHandler<byte[]>? DataReceived;
    public event EventHandler<SessionStateChangedArgs>? StateChanged;
    public event EventHandler<string>? TitleChanged;

    // Port forwarding
    private readonly List<ForwardedPortLocal> _localForwards = new();
    private readonly List<ForwardedPortRemote> _remoteForwards = new();

    internal SshSession(SshClient client, ConnectionProfile profile)
    {
        _client = client;
        Profile = profile;
    }

    internal async Task StartShellAsync(int cols, int rows, CancellationToken ct)
    {
        SetState(SessionState.Connecting);
        await Task.Run(() =>
        {
            var modes = new Dictionary<TerminalModes, uint>
            {
                { TerminalModes.ECHO, 1 },
                { TerminalModes.ICANON, 1 },
                { TerminalModes.ISIG, 1 },
            };
            _shell = _client.CreateShellStream("xterm-256color", (uint)cols, (uint)rows, 0, 0, 8192, modes);
        }, ct);

        ConnectedAt = DateTime.UtcNow;
        SetState(SessionState.Connected);
        _readTask = ReadLoopAsync(_cts.Token);
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buf = new byte[16384];
        try
        {
            while (!ct.IsCancellationRequested && _shell is not null)
            {
                int n = await _shell.ReadAsync(buf, 0, buf.Length, ct);
                if (n <= 0) break;
                BytesReceived += n;
                var data = new byte[n];
                Buffer.BlockCopy(buf, 0, data, 0, n);
                DataReceived?.Invoke(this, data);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Serilog.Log.Warning(ex, "SSH read loop ended for {Id}", Id); }
        finally
        {
            if (State == SessionState.Connected)
                SetState(SessionState.Disconnected, "Connection closed by remote");
        }
    }

    public async Task WriteAsync(byte[] data, CancellationToken ct = default)
    {
        if (_shell is null || State != SessionState.Connected) return;
        await _shell.WriteAsync(data, 0, data.Length, ct);
        BytesSent += data.Length;
    }

    public async Task WriteTextAsync(string text, CancellationToken ct = default)
        => await WriteAsync(Encoding.UTF8.GetBytes(text), ct);

    public async Task ResizeAsync(int cols, int rows)
    {
        if (_shell is null) return;
        // SSH.NET's ShellStream doesn't expose SendWindowChangeRequest publicly.
        // We use reflection to call the underlying channel's SendWindowChangeRequest.
        await Task.Run(() =>
        {
            try
            {
                var channelField = typeof(ShellStream)
                    .GetField("_channel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?? typeof(ShellStream).GetField("Channel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (channelField?.GetValue(_shell) is { } channel)
                {
                    var method = channel.GetType().GetMethod("SendWindowChangeRequest",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    method?.Invoke(channel, new object[] { (uint)cols, (uint)rows, 0u, 0u });
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug(ex, "Terminal resize via reflection failed; size change may not propagate");
            }
        });
    }

    public void StartPortForwards(IEnumerable<PortForwardRule> rules)
    {
        foreach (var rule in rules.Where(r => r.AutoStart))
        {
            try
            {
                if (rule.Direction == ForwardDirection.Local)
                {
                    var fwd = new ForwardedPortLocal(rule.LocalHost, (uint)rule.LocalPort, rule.RemoteHost, (uint)rule.RemotePort);
                    _client.AddForwardedPort(fwd);
                    fwd.Start();
                    _localForwards.Add(fwd);
                    Serilog.Log.Information("Started local forward {Local}:{LPort} → {Remote}:{RPort}",
                        rule.LocalHost, rule.LocalPort, rule.RemoteHost, rule.RemotePort);
                }
                else
                {
                    var fwd = new ForwardedPortRemote(rule.RemoteHost, (uint)rule.RemotePort, rule.LocalHost, (uint)rule.LocalPort);
                    _client.AddForwardedPort(fwd);
                    fwd.Start();
                    _remoteForwards.Add(fwd);
                }
            }
            catch (Exception ex) { Serilog.Log.Error(ex, "Failed to start port forward"); }
        }
    }

    public async Task<string> ExecuteCommandAsync(string command, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            using var cmd = _client.CreateCommand(command);
            cmd.Execute();
            return cmd.Result + cmd.Error;
        }, ct);
    }

    private void SetState(SessionState next, string? message = null)
    {
        var old = State;
        State = next;
        StateChanged?.Invoke(this, new SessionStateChangedArgs(old, next, message));
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_readTask is not null) await _readTask.ConfigureAwait(false);

        foreach (var fwd in _localForwards) { try { fwd.Stop(); fwd.Dispose(); } catch { } }
        foreach (var fwd in _remoteForwards) { try { fwd.Stop(); fwd.Dispose(); } catch { } }

        _shell?.Dispose();
        _client.Dispose();
        _cts.Dispose();
    }
}

public class SshConnectionService
{
    private readonly KnownHostsService _knownHosts;
    private readonly SettingsService _settings;

    public SshConnectionService(KnownHostsService knownHosts, SettingsService settings)
    {
        _knownHosts = knownHosts;
        _settings = settings;
    }

    /// <summary>
    /// Creates and connects an SSH session. Throws on failure.
    /// Host key verification is done via a callback that may prompt the user.
    /// </summary>
    public async Task<SshSession> ConnectAsync(
        ConnectionProfile profile,
        int cols, int rows,
        Func<string, string, string, Task<bool>> hostKeyCallback,
        CancellationToken ct = default)
    {
        Serilog.Log.Information("Connecting to {Host}:{Port} as {User}", profile.Host, profile.Port, profile.Username);

        var authMethods = BuildAuthMethods(profile);
        var connectionInfo = new ConnectionInfo(profile.Host, profile.Port, profile.Username, authMethods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(30),
            RetryAttempts = 1,
        };

        var client = new SshClient(connectionInfo);
        // HostKeyReceived fires on the SSH background thread — must be synchronous.
        // We block that thread by dispatching to the UI thread and waiting for a result.
        client.HostKeyReceived += (sender, e) =>
        {
            var fingerprint = BitConverter.ToString(e.FingerPrint).Replace("-", ":").ToLowerInvariant();
            var keyType = e.HostKeyName;
            var result = _knownHosts.Verify(profile.Host, profile.Port, keyType, fingerprint);

            bool trust;
            switch (result)
            {
                case HostVerificationResult.Trusted:
                    e.CanTrust = true;
                    return;
                case HostVerificationResult.NewHost:
                    trust = System.Windows.Application.Current.Dispatcher.Invoke(
                        () => hostKeyCallback(fingerprint, keyType, "new").GetAwaiter().GetResult());
                    e.CanTrust = trust;
                    if (trust) _knownHosts.AddHost(profile.Host, profile.Port, keyType, fingerprint);
                    break;
                case HostVerificationResult.Changed:
                    trust = System.Windows.Application.Current.Dispatcher.Invoke(
                        () => hostKeyCallback(fingerprint, keyType, "changed").GetAwaiter().GetResult());
                    e.CanTrust = trust;
                    if (trust) _knownHosts.AddHost(profile.Host, profile.Port, keyType, fingerprint);
                    break;
                default:
                    e.CanTrust = false;
                    break;
            }
        };

        await Task.Run(() => client.Connect(), ct);
        var session = new SshSession(client, profile);
        await session.StartShellAsync(cols, rows, ct);

        if (profile.PortForwardRules.Any())
            session.StartPortForwards(profile.PortForwardRules);

        return session;
    }

    private static List<AuthenticationMethod> BuildAuthMethods(ConnectionProfile profile)
    {
        if (profile.AuthMethod == AuthMethod.PrivateKey && !string.IsNullOrEmpty(profile.PrivateKeyPath))
        {
            var passphrase = ProfileService.DecryptPassword(profile.EncryptedPassphrase);
            var keyFile = passphrase is not null
                ? new PrivateKeyFile(profile.PrivateKeyPath, passphrase)
                : new PrivateKeyFile(profile.PrivateKeyPath);
            return new List<AuthenticationMethod> { new PrivateKeyAuthenticationMethod(profile.Username, keyFile) };
        }
        var password = ProfileService.DecryptPassword(profile.EncryptedPassword) ?? string.Empty;
        return new List<AuthenticationMethod> { new PasswordAuthenticationMethod(profile.Username, password) };
    }
}
