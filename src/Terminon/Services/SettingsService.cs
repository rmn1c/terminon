using System.Text.Json;
using Terminon.Models;

namespace Terminon.Services;

public class SettingsService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Terminon", "settings.json");

    private AppSettings _current = new();

    public AppSettings Current => _current;

    public void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                _current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to load settings, using defaults");
            _current = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var json = JsonSerializer.Serialize(_current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to save settings");
        }
    }
}
