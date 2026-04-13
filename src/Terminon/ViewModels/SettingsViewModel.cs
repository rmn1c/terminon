using Terminon.Infrastructure;
using Terminon.Models;
using Terminon.Services;

namespace Terminon.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private AppSettings _edited = new();

    public ThemeMode Theme { get => _edited.Theme; set { _edited.Theme = value; OnPropertyChanged(); } }
    public string FontFamily { get => _edited.FontFamily; set { _edited.FontFamily = value; OnPropertyChanged(); } }
    public double FontSize { get => _edited.FontSize; set { _edited.FontSize = value; OnPropertyChanged(); } }
    public int ScrollbackLines { get => _edited.ScrollbackLines; set { _edited.ScrollbackLines = value; OnPropertyChanged(); } }
    public BellMode BellMode { get => _edited.BellMode; set { _edited.BellMode = value; OnPropertyChanged(); } }
    public bool CloseTabOnDisconnect { get => _edited.CloseTabOnDisconnect; set { _edited.CloseTabOnDisconnect = value; OnPropertyChanged(); } }
    public bool ConfirmOnClose { get => _edited.ConfirmOnClose; set { _edited.ConfirmOnClose = value; OnPropertyChanged(); } }
    public bool MinimizeToTray { get => _edited.MinimizeToTray; set { _edited.MinimizeToTray = value; OnPropertyChanged(); } }
    public bool BracketedPaste { get => _edited.BracketedPaste; set { _edited.BracketedPaste = value; OnPropertyChanged(); } }
    public int CursorBlinkRateMs { get => _edited.CursorBlinkRateMs; set { _edited.CursorBlinkRateMs = value; OnPropertyChanged(); } }

    public List<ThemeMode> Themes { get; } = Enum.GetValues<ThemeMode>().ToList();
    public List<BellMode> BellModes { get; } = Enum.GetValues<BellMode>().ToList();
    public List<string> FontFamilies { get; } = new()
    {
        "Cascadia Code", "Cascadia Mono", "Consolas", "Courier New",
        "Fira Code", "JetBrains Mono", "Lucida Console", "Menlo",
        "Monaco", "Source Code Pro", "Ubuntu Mono"
    };

    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ResetCommand { get; }

    public bool? DialogResult { get; private set; }
    public event EventHandler? RequestClose;

    public SettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        LoadFromService();

        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(Cancel);
        ResetCommand = new RelayCommand(Reset);
    }

    private void LoadFromService()
    {
        var s = _settingsService.Current;
        _edited = new AppSettings
        {
            Theme = s.Theme,
            FontFamily = s.FontFamily,
            FontSize = s.FontSize,
            ScrollbackLines = s.ScrollbackLines,
            BellMode = s.BellMode,
            CloseTabOnDisconnect = s.CloseTabOnDisconnect,
            ConfirmOnClose = s.ConfirmOnClose,
            MinimizeToTray = s.MinimizeToTray,
            BracketedPaste = s.BracketedPaste,
            CursorBlinkRateMs = s.CursorBlinkRateMs,
        };
        OnPropertyChanged(string.Empty); // refresh all
    }

    private void Save()
    {
        var s = _settingsService.Current;
        s.Theme = _edited.Theme;
        s.FontFamily = _edited.FontFamily;
        s.FontSize = _edited.FontSize;
        s.ScrollbackLines = _edited.ScrollbackLines;
        s.BellMode = _edited.BellMode;
        s.CloseTabOnDisconnect = _edited.CloseTabOnDisconnect;
        s.ConfirmOnClose = _edited.ConfirmOnClose;
        s.MinimizeToTray = _edited.MinimizeToTray;
        s.BracketedPaste = _edited.BracketedPaste;
        s.CursorBlinkRateMs = _edited.CursorBlinkRateMs;
        _settingsService.Save();
        DialogResult = true;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    private void Cancel() { DialogResult = false; RequestClose?.Invoke(this, EventArgs.Empty); }
    private void Reset() { _edited = new AppSettings(); OnPropertyChanged(string.Empty); }
}
