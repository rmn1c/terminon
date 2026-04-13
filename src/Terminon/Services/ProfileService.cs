using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Terminon.Models;

namespace Terminon.Services;

public class ProfileData
{
    public List<ConnectionFolder> Folders { get; set; } = new();
    public List<ConnectionProfile> Profiles { get; set; } = new();
}

public class ProfileService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Terminon", "profiles.json");

    private ProfileData _data = new();

    public IReadOnlyList<ConnectionFolder> Folders => _data.Folders;
    public IReadOnlyList<ConnectionProfile> Profiles => _data.Profiles;

    public void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                _data = JsonSerializer.Deserialize<ProfileData>(json) ?? new ProfileData();
            }
            if (!_data.Folders.Any(f => f.Id == string.Empty))
                _data.Folders.Insert(0, new ConnectionFolder { Id = string.Empty, Name = "Default" });
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to load profiles");
            _data = new ProfileData();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to save profiles");
        }
    }

    public void AddProfile(ConnectionProfile profile)
    {
        _data.Profiles.Add(profile);
        Save();
    }

    public void UpdateProfile(ConnectionProfile profile)
    {
        int idx = _data.Profiles.FindIndex(p => p.Id == profile.Id);
        if (idx >= 0) _data.Profiles[idx] = profile;
        else _data.Profiles.Add(profile);
        Save();
    }

    public void DeleteProfile(string id)
    {
        _data.Profiles.RemoveAll(p => p.Id == id);
        Save();
    }

    public void AddFolder(ConnectionFolder folder)
    {
        _data.Folders.Add(folder);
        Save();
    }

    public void DeleteFolder(string id)
    {
        _data.Folders.RemoveAll(f => f.Id == id);
        _data.Profiles.RemoveAll(p => p.FolderId == id);
        Save();
    }

    public List<ConnectionProfile> GetRecent(int max = 10)
        => _data.Profiles
            .Where(p => p.LastConnected > DateTime.MinValue)
            .OrderByDescending(p => p.LastConnected)
            .Take(max)
            .ToList();

    /// <summary>Encrypts a password using DPAPI (Windows only).</summary>
    public static string? EncryptPassword(string? password)
    {
        if (string.IsNullOrEmpty(password)) return null;
        var bytes = Encoding.UTF8.GetBytes(password);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    /// <summary>Decrypts a DPAPI-encrypted password.</summary>
    public static string? DecryptPassword(string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return null;
        try
        {
            var bytes = Convert.FromBase64String(encrypted);
            var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            return null;
        }
    }
}
