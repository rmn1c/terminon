namespace Terminon.Models;

public enum ThemeMode { Dark, Light }
public enum BellMode { None, Visual, Sound }

public class AppSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.Dark;
    public string FontFamily { get; set; } = "Cascadia Code";
    public double FontSize { get; set; } = 14.0;
    public int ScrollbackLines { get; set; } = 10_000;
    public BellMode BellMode { get; set; } = BellMode.Visual;
    public bool CloseTabOnDisconnect { get; set; } = false;
    public bool ConfirmOnClose { get; set; } = true;
    public bool MinimizeToTray { get; set; } = false;
    public bool StartMinimized { get; set; } = false;
    public int CursorBlinkRateMs { get; set; } = 530;
    public bool BracketedPaste { get; set; } = true;

    // Colors for dark theme overrides
    public string? TerminalBackground { get; set; }
    public string? TerminalForeground { get; set; }

    public string LogDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Terminon", "Logs");
}
