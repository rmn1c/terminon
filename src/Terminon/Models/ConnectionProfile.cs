using System.Text.Json.Serialization;

namespace Terminon.Models;

public enum AuthMethod
{
    Password,
    PrivateKey
}

public class ConnectionProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string Username { get; set; } = string.Empty;
    public AuthMethod AuthMethod { get; set; } = AuthMethod.Password;

    /// <summary>Encrypted password stored via DPAPI (Base64). Never stored in plain text.</summary>
    public string? EncryptedPassword { get; set; }

    public string? PrivateKeyPath { get; set; }

    /// <summary>Encrypted passphrase for the private key.</summary>
    public string? EncryptedPassphrase { get; set; }

    public string FolderId { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime LastConnected { get; set; }
    public List<PortForwardRule> PortForwardRules { get; set; } = new();

    [JsonIgnore]
    public bool IsRecent => LastConnected > DateTime.MinValue;
}
