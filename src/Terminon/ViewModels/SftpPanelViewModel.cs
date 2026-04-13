using System.Collections.ObjectModel;
using Terminon.Infrastructure;
using Terminon.Models;
using Terminon.Services;

namespace Terminon.ViewModels;

public class TransferItem : ViewModelBase
{
    private double _progress;
    private string _status = "Pending";
    private bool _isComplete;
    private bool _isFailed;

    public string FileName { get; init; } = string.Empty;
    public string RemotePath { get; init; } = string.Empty;
    public string LocalPath { get; init; } = string.Empty;
    public bool IsUpload { get; init; }
    public double Progress { get => _progress; set => SetProperty(ref _progress, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public bool IsComplete { get => _isComplete; set => SetProperty(ref _isComplete, value); }
    public bool IsFailed { get => _isFailed; set => SetProperty(ref _isFailed, value); }
}

public class SftpPanelViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly SftpService _sftp;
    private string _currentPath = "/";
    private SftpEntry? _selectedEntry;
    private bool _isLoading;
    private bool _isConnected;
    private string _statusMessage = string.Empty;

    public string CurrentPath { get => _currentPath; private set => SetProperty(ref _currentPath, value); }
    public SftpEntry? SelectedEntry { get => _selectedEntry; set { if (SetProperty(ref _selectedEntry, value)) RefreshCommands(); } }
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    public bool IsConnected { get => _isConnected; private set => SetProperty(ref _isConnected, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    public ObservableCollection<SftpEntry> Entries { get; } = new();
    public ObservableCollection<TransferItem> Transfers { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand NavigateUpCommand { get; }
    public AsyncRelayCommand<SftpEntry> OpenEntryCommand { get; }
    public AsyncRelayCommand DownloadCommand { get; }
    public AsyncRelayCommand UploadCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public AsyncRelayCommand NewFolderCommand { get; }

    public event Func<string, Task<string?>>? RequestSaveDialog;
    public event Func<string[], Task<string[]?>>? RequestOpenDialog;
    public event Func<string, Task<string?>>? RequestInputDialog;

    public SftpPanelViewModel(SftpService sftp)
    {
        _sftp = sftp;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => IsConnected);
        NavigateUpCommand = new AsyncRelayCommand(NavigateUpAsync, () => IsConnected && CurrentPath != "/");
        OpenEntryCommand = new AsyncRelayCommand<SftpEntry>(OpenEntryAsync);
        DownloadCommand = new AsyncRelayCommand(DownloadSelectedAsync, () => IsConnected && SelectedEntry?.EntryType == SftpEntryType.File);
        UploadCommand = new AsyncRelayCommand(UploadAsync, () => IsConnected);
        DeleteCommand = new AsyncRelayCommand(DeleteSelectedAsync, () => IsConnected && SelectedEntry is not null);
        NewFolderCommand = new AsyncRelayCommand(NewFolderAsync, () => IsConnected);
    }

    public async Task ConnectAsync(ConnectionProfile profile, CancellationToken ct = default)
    {
        try
        {
            await _sftp.ConnectAsync(profile, ct);
            IsConnected = true;
            CurrentPath = await _sftp.GetHomeDirectoryAsync(ct);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "SFTP connect failed");
        }
    }

    public async Task RefreshAsync()
    {
        if (!IsConnected) return;
        IsLoading = true;
        StatusMessage = "Loading…";
        try
        {
            var entries = await _sftp.ListDirectoryAsync(CurrentPath);
            Entries.Clear();
            foreach (var e in entries) Entries.Add(e);
            StatusMessage = $"{entries.Count} items";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    private async Task NavigateUpAsync()
    {
        var parent = Path.GetDirectoryName(CurrentPath.TrimEnd('/'))?.Replace('\\', '/') ?? "/";
        if (string.IsNullOrEmpty(parent)) parent = "/";
        CurrentPath = parent;
        await RefreshAsync();
    }

    private async Task OpenEntryAsync(SftpEntry? entry)
    {
        if (entry is null) return;
        if (entry.EntryType == SftpEntryType.Directory)
        {
            CurrentPath = entry.FullPath;
            await RefreshAsync();
        }
    }

    private async Task DownloadSelectedAsync()
    {
        if (SelectedEntry is null) return;
        var localPath = await RequestSaveDialog?.Invoke(SelectedEntry.Name) ?? null;
        if (localPath is null) return;

        var transfer = new TransferItem
        {
            FileName = SelectedEntry.Name,
            RemotePath = SelectedEntry.FullPath,
            LocalPath = localPath,
            IsUpload = false,
            Status = "Downloading…"
        };
        Transfers.Add(transfer);

        try
        {
            await _sftp.DownloadFileAsync(SelectedEntry.FullPath, localPath,
                new Progress<double>(p => transfer.Progress = p * 100));
            transfer.IsComplete = true;
            transfer.Status = "Complete";
        }
        catch (Exception ex)
        {
            transfer.IsFailed = true;
            transfer.Status = $"Failed: {ex.Message}";
        }
    }

    private async Task UploadAsync()
    {
        var files = await RequestOpenDialog?.Invoke(Array.Empty<string>()) ?? null;
        if (files is null || files.Length == 0) return;

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var remotePath = CurrentPath.TrimEnd('/') + "/" + fileName;
            var transfer = new TransferItem { FileName = fileName, RemotePath = remotePath, LocalPath = file, IsUpload = true, Status = "Uploading…" };
            Transfers.Add(transfer);

            try
            {
                await _sftp.UploadFileAsync(file, remotePath,
                    new Progress<double>(p => transfer.Progress = p * 100));
                transfer.IsComplete = true;
                transfer.Status = "Complete";
            }
            catch (Exception ex) { transfer.IsFailed = true; transfer.Status = $"Failed: {ex.Message}"; }
        }

        await RefreshAsync();
    }

    private async Task DeleteSelectedAsync()
    {
        if (SelectedEntry is null) return;
        try
        {
            if (SelectedEntry.EntryType == SftpEntryType.Directory)
                await _sftp.DeleteDirectoryAsync(SelectedEntry.FullPath);
            else
                await _sftp.DeleteFileAsync(SelectedEntry.FullPath);
            await RefreshAsync();
        }
        catch (Exception ex) { StatusMessage = $"Delete failed: {ex.Message}"; }
    }

    private async Task NewFolderAsync()
    {
        var name = await RequestInputDialog?.Invoke("New folder name:") ?? null;
        if (string.IsNullOrEmpty(name)) return;
        try
        {
            await _sftp.CreateDirectoryAsync(CurrentPath.TrimEnd('/') + "/" + name);
            await RefreshAsync();
        }
        catch (Exception ex) { StatusMessage = $"Failed: {ex.Message}"; }
    }

    private void RefreshCommands()
    {
        DownloadCommand.RaiseCanExecuteChanged();
        DeleteCommand.RaiseCanExecuteChanged();
    }

    public async ValueTask DisposeAsync()
    {
        await _sftp.DisposeAsync();
    }
}
