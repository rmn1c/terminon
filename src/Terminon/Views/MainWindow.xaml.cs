using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Terminon.Controls;
using Terminon.Infrastructure;
using Terminon.Models;
using Terminon.Services;
using Terminon.Terminal;
using Terminon.ViewModels;

namespace Terminon.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    // Expose to XAML bindings for font settings
    public FontFamily TerminalFontFamily { get; private set; } = new FontFamily("Cascadia Code");
    public double TerminalFontSize { get; private set; } = 14.0;

    public MainWindow()
    {
        InitializeComponent();

        _vm = new MainViewModel(
            ServiceLocator.Get<SshConnectionService>(),
            ServiceLocator.Get<ProfileService>(),
            ServiceLocator.Get<SettingsService>(),
            ServiceLocator.Get<SftpService>(),
            ServiceLocator.Get<KeyService>());

        DataContext = _vm;
        _vm.Initialize();

        // Wire dialog events
        _vm.ShowConnectionDialog += ShowConnectionDialogAsync;
        _vm.ShowSettingsDialog += ShowSettingsDialogAsync;
        _vm.ShowKeygenDialog += ShowKeygenDialogAsync;
        _vm.ShowHostKeyDialog += ShowHostKeyDialogAsync;

        // Wire search bar
        SearchBarControl.NextRequested += (_, _) => TermCtrl.SearchNext();
        SearchBarControl.PrevRequested += (_, _) => TermCtrl.SearchPrev();
        SearchBarControl.CloseRequested += (_, _) =>
        {
            if (_vm.ActiveTab is not null) _vm.ActiveTab.IsSearchVisible = false;
        };

        // Apply settings
        ApplySettings();

        // Property change monitoring for search text sync
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ActiveTab))
            {
                SyncSftpColumnVisibility();
                if (_vm.ActiveTab is not null)
                    _vm.ActiveTab.PropertyChanged += ActiveTab_PropertyChanged;
            }
        };

        SetupSystemTray();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        TermCtrl.Focus();
    }

    private void ActiveTab_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionTabViewModel.IsSearchVisible) && _vm.ActiveTab?.IsSearchVisible == true)
            Dispatcher.InvokeAsync(() => SearchBarControl.FocusSearchBox());

        if (e.PropertyName == nameof(SessionTabViewModel.SearchText))
            TermCtrl.Search(_vm.ActiveTab?.SearchText ?? string.Empty);

        if (e.PropertyName == nameof(SessionTabViewModel.IsSftpVisible))
            SyncSftpColumnVisibility();
    }

    private void SyncSftpColumnVisibility()
    {
        bool show = _vm.ActiveTab?.IsSftpVisible ?? false;
        SftpCol.Width = show ? new GridLength(380) : new GridLength(0);
        SftpSplitterCol.Width = show ? new GridLength(4) : new GridLength(0);
    }

    private void ApplySettings()
    {
        var settings = ServiceLocator.Get<SettingsService>().Current;
        TerminalFontFamily = new FontFamily(settings.FontFamily);
        TerminalFontSize = settings.FontSize;

        // Apply theme
        var dict = Application.Current.Resources.MergedDictionaries;
        dict.Clear();
        var themePath = settings.Theme == ThemeMode.Dark
            ? "Resources/Themes/DarkTheme.xaml"
            : "Resources/Themes/LightTheme.xaml";
        dict.Add(new ResourceDictionary { Source = new Uri(themePath, UriKind.Relative) });
        dict.Add(new ResourceDictionary { Source = new Uri("Resources/Styles/CommonStyles.xaml", UriKind.Relative) });

        // Apply color scheme and fonts to terminal
        TermCtrl.ColorScheme = settings.Theme == ThemeMode.Dark ? ColorScheme.Dark : ColorScheme.Light;
        TermCtrl.FontFamily = TerminalFontFamily;
        TermCtrl.FontSize = TerminalFontSize;
        TermCtrl.CursorBlinkRate = settings.CursorBlinkRateMs;
    }

    // ─── Dialog handlers ──────────────────────────────────────────────────────

    private async Task<ConnectionProfile?> ShowConnectionDialogAsync(ConnectionProfile? prefill)
    {
        var dlg = new ConnectionDialog(prefill, ServiceLocator.Get<ProfileService>());
        dlg.Owner = this;
        dlg.ShowDialog();
        return dlg.ResultProfile;
    }

    private async Task ShowSettingsDialogAsync()
    {
        var dlg = new SettingsDialog(ServiceLocator.Get<SettingsService>());
        dlg.Owner = this;
        if (dlg.ShowDialog() == true)
            ApplySettings();
    }

    private async Task ShowKeygenDialogAsync()
    {
        var dlg = new KeyGenerationDialog(ServiceLocator.Get<KeyService>());
        dlg.Owner = this;
        dlg.ShowDialog();
    }

    private async Task<bool> ShowHostKeyDialogAsync(string fingerprint, string keyType, string reason)
    {
        var dlg = new KnownHostDialog(fingerprint, keyType, reason);
        dlg.Owner = this;
        return dlg.ShowDialog() == true;
    }

    // ─── Event handlers ────────────────────────────────────────────────────────

    private void TabHeader_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is SessionTabViewModel tab)
        {
            _vm.ActiveTab = tab;
            TermCtrl.Focus();
        }
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is SessionTabViewModel tab)
            ((System.Windows.Input.ICommand)_vm.CloseSpecificTabCommand).Execute(tab);
    }

    private void ConnectFromSidebar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ConnectionProfile profile)
            _ = _vm.ConnectProfileAsync(profile);
    }

    private void TreeItem_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ConnectionTreeItem item
            && item.Profile is not null)
        {
            _ = _vm.ConnectProfileAsync(item.Profile);
            e.Handled = true;
        }
    }

    // ─── System Tray ──────────────────────────────────────────────────────────

    private void SetupSystemTray()
    {
        var settings = ServiceLocator.Get<SettingsService>().Current;
        if (!settings.MinimizeToTray) return;

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Terminon SSH Client",
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => { Show(); WindowState = WindowState.Normal; };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => { Show(); WindowState = WindowState.Normal; });
        menu.Items.Add("Exit", null, (_, _) => { _trayIcon.Visible = false; Application.Current.Shutdown(); });
        _trayIcon.ContextMenuStrip = menu;
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        var settings = ServiceLocator.Get<SettingsService>().Current;
        if (settings.MinimizeToTray && WindowState == WindowState.Minimized)
        {
            Hide();
            _trayIcon!.Visible = true;
        }
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var settings = ServiceLocator.Get<SettingsService>().Current;
        if (settings.ConfirmOnClose && _vm.Tabs.Count > 0)
        {
            var result = MessageBox.Show(
                $"You have {_vm.Tabs.Count} active session(s). Close all?",
                "Confirm Exit", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) { e.Cancel = true; return; }
        }

        _trayIcon?.Dispose();

        // Dispose all sessions
        foreach (var tab in _vm.Tabs.ToList())
            await tab.DisposeAsync();
    }
}
