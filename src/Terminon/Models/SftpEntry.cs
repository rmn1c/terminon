namespace Terminon.Models;

public enum SftpEntryType { File, Directory, SymbolicLink, Other }

public class SftpEntry
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public SftpEntryType EntryType { get; set; }
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public string Permissions { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public bool IsHidden => Name.StartsWith('.');

    public string SizeDisplay => EntryType == SftpEntryType.Directory
        ? "<DIR>"
        : FormatSize(Size);

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };
}
