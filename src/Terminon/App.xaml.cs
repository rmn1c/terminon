using System.Windows;
using Serilog;
using Terminon.Infrastructure;
using Terminon.Models;
using Terminon.Services;
using Terminon.Views;

namespace Terminon;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Configure Serilog before anything else
        ConfigureLogging();

        Log.Information("Terminon starting up");

        // Set up global exception handlers
        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Fatal(ex.Exception, "Unhandled UI exception");
            MessageBox.Show($"An unexpected error occurred:\n\n{ex.Exception.Message}",
                "Terminon Error", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            if (ex.ExceptionObject is Exception exception)
                Log.Fatal(exception, "Unhandled domain exception");
        };

        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            Log.Error(ex.Exception, "Unobserved task exception");
            ex.SetObserved();
        };

        // Initialize services
        ServiceLocator.Initialize();

        // Load settings to determine startup behavior
        var settings = ServiceLocator.Get<SettingsService>();
        settings.Load();

        // Show main window
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;

        if (settings.Current.StartMinimized)
            mainWindow.WindowState = WindowState.Minimized;

        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Terminon shutting down");
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static void ConfigureLogging()
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Terminon", "Logs");
        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Debug()
            .WriteTo.File(
                path: Path.Combine(logDir, "terminon-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}
