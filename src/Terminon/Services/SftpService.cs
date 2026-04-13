using Renci.SshNet;
using Renci.SshNet.Sftp;
using Terminon.Models;

namespace Terminon.Services;

public class SftpService : IAsyncDisposable
{
    private SftpClient? _client;

    public bool IsConnected => _client?.IsConnected ?? false;

    public async Task ConnectAsync(ConnectionProfile profile, CancellationToken ct = default)
    {
        Disconnect();
        var authMethods = BuildAuthMethods(profile);
        var info = new ConnectionInfo(profile.Host, profile.Port, profile.Username, authMethods);
        _client = new SftpClient(info);
        await Task.Run(() => _client.Connect(), ct);
        Serilog.Log.Information("SFTP connected to {Host}", profile.Host);
    }

    public void Disconnect()
    {
        if (_client is not null)
        {
            try { _client.Disconnect(); } catch { }
            _client.Dispose();
            _client = null;
        }
    }

    public async Task<List<SftpEntry>> ListDirectoryAsync(string path, CancellationToken ct = default)
    {
        EnsureConnected();
        var files = await Task.Run(() => _client!.ListDirectory(path).ToList(), ct);
        return files
            .Where(f => f.Name != ".")
            .OrderByDescending(f => f.IsDirectory)
            .ThenBy(f => f.Name)
            .Select(MapEntry)
            .ToList();
    }

    public async Task DownloadFileAsync(
        string remotePath, string localPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        EnsureConnected();
        var fileInfo = await Task.Run(() => _client!.Get(remotePath), ct);
        long totalBytes = fileInfo.Length;
        long received = 0;

        await Task.Run(() =>
        {
            using var fs = File.Create(localPath);
            _client!.DownloadFile(remotePath, fs, bytes =>
            {
                received = (long)bytes;
                progress?.Report(totalBytes > 0 ? (double)received / totalBytes : 0);
                if (ct.IsCancellationRequested) throw new OperationCanceledException();
            });
        }, ct);
    }

    public async Task UploadFileAsync(
        string localPath, string remotePath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        EnsureConnected();
        long totalBytes = new FileInfo(localPath).Length;
        long sent = 0;

        await Task.Run(() =>
        {
            using var fs = File.OpenRead(localPath);
            _client!.UploadFile(fs, remotePath, bytes =>
            {
                sent = (long)bytes;
                progress?.Report(totalBytes > 0 ? (double)sent / totalBytes : 0);
                if (ct.IsCancellationRequested) throw new OperationCanceledException();
            });
        }, ct);
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken ct = default)
    {
        EnsureConnected();
        await Task.Run(() => _client!.CreateDirectory(path), ct);
    }

    public async Task DeleteFileAsync(string path, CancellationToken ct = default)
    {
        EnsureConnected();
        await Task.Run(() => _client!.DeleteFile(path), ct);
    }

    public async Task DeleteDirectoryAsync(string path, CancellationToken ct = default)
    {
        EnsureConnected();
        await Task.Run(() => _client!.DeleteDirectory(path), ct);
    }

    public async Task RenameAsync(string oldPath, string newPath, CancellationToken ct = default)
    {
        EnsureConnected();
        await Task.Run(() => _client!.RenameFile(oldPath, newPath), ct);
    }

    public async Task<string> GetHomeDirectoryAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        return await Task.Run(() => _client!.WorkingDirectory, ct);
    }

    private void EnsureConnected()
    {
        if (_client is null || !_client.IsConnected)
            throw new InvalidOperationException("SFTP not connected");
    }

    private static SftpEntry MapEntry(ISftpFile f) => new()
    {
        Name = f.Name,
        FullPath = f.FullName,
        EntryType = f.IsDirectory ? SftpEntryType.Directory
                   : f.IsSymbolicLink ? SftpEntryType.SymbolicLink
                   : SftpEntryType.File,
        Size = f.Length,
        LastModified = f.LastWriteTime,
        Permissions = string.Empty,  // SSH.NET ISftpFile doesn't expose raw permission string
        Owner = string.Empty,
        Group = string.Empty,
    };

    private static AuthenticationMethod[] BuildAuthMethods(ConnectionProfile profile)
    {
        if (profile.AuthMethod == AuthMethod.PrivateKey && !string.IsNullOrEmpty(profile.PrivateKeyPath))
        {
            var pass = ProfileService.DecryptPassword(profile.EncryptedPassphrase);
            var keyFile = pass is not null
                ? new PrivateKeyFile(profile.PrivateKeyPath, pass)
                : new PrivateKeyFile(profile.PrivateKeyPath);
            return new AuthenticationMethod[] { new PrivateKeyAuthenticationMethod(profile.Username, keyFile) };
        }
        var pwd = ProfileService.DecryptPassword(profile.EncryptedPassword) ?? string.Empty;
        return new AuthenticationMethod[] { new PasswordAuthenticationMethod(profile.Username, pwd) };
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Run(Disconnect);
    }
}
