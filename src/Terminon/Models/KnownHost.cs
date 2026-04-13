namespace Terminon.Models;

public class KnownHost
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string KeyType { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public string AddedBy { get; set; } = Environment.UserName;

    public string HostKey => Port == 22 ? Host : $"[{Host}]:{Port}";
}
