using System.Text.Json;
using Terminon.Models;

namespace Terminon.Services;

public enum HostVerificationResult { Trusted, NewHost, Changed, Rejected }

public class KnownHostsService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Terminon", "known_hosts.json");

    private List<KnownHost> _hosts = new();

    public IReadOnlyList<KnownHost> Hosts => _hosts;

    public void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                _hosts = JsonSerializer.Deserialize<List<KnownHost>>(json) ?? new();
            }
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Failed to load known hosts"); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_hosts, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Serilog.Log.Error(ex, "Failed to save known hosts"); }
    }

    public HostVerificationResult Verify(string host, int port, string keyType, string fingerprint)
    {
        var existing = _hosts.FirstOrDefault(h => h.Host == host && h.Port == port);
        if (existing is null) return HostVerificationResult.NewHost;
        return existing.Fingerprint == fingerprint
            ? HostVerificationResult.Trusted
            : HostVerificationResult.Changed;
    }

    public void AddHost(string host, int port, string keyType, string fingerprint)
    {
        _hosts.RemoveAll(h => h.Host == host && h.Port == port);
        _hosts.Add(new KnownHost { Host = host, Port = port, KeyType = keyType, Fingerprint = fingerprint });
        Save();
    }

    public void RemoveHost(string host, int port)
    {
        _hosts.RemoveAll(h => h.Host == host && h.Port == port);
        Save();
    }
}
