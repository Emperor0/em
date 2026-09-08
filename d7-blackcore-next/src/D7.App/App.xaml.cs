using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using D7.App.Services;
using D7.Core.Foundation;
using D7.Core.Logging;
using D7.Hardware.Discovery;
using D7.Hardware.Telemetry;

namespace D7.App;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private JsonLineLogger? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, @"Local\D7_BLACKCORE_NEXT_SINGLE_INSTANCE", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "D7 BLACKCORE يعمل بالفعل. استخدم النافذة المفتوحة.",
                "D7 BLACKCORE",
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                MessageBoxResult.OK,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            Shutdown(0);
            return;
        }

        var paths = new AppPaths();
        paths.EnsureCreated();
        _logger = new JsonLineLogger(paths);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var safeMode = e.Args.Any(x => string.Equals(x, "--safe-mode", StringComparison.OrdinalIgnoreCase));
        var bootstrap = new BootstrapService(paths, _logger, new HttpClient());
        var hardwareDiscovery = new WindowsHardwareDiscoveryService();
        var telemetry = new SystemTelemetrySampler();
        var window = new MainWindow(bootstrap, _logger, hardwareDiscovery, telemetry, safeMode);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_singleInstanceMutex is not null)
            {
                _singleInstanceMutex.ReleaseMutex();
                _singleInstanceMutex.Dispose();
            }
        }
        catch (ApplicationException)
        {
            // The mutex may already be released during abnormal shutdown.
        }
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _ = LogCrashAsync("UI", e.Exception);
        ShowControlledError();
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            _ = LogCrashAsync("Domain", ex);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _ = LogCrashAsync("Task", e.Exception);
        e.SetObserved();
    }

    private async Task LogCrashAsync(string source, Exception exception)
    {
        if (_logger is null) return;
        try
        {
            await _logger.WriteAsync(
                "Crash",
                Guid.NewGuid().ToString("N"),
                "Critical",
                "خطأ غير متوقع",
                new { source, exception.Message, exception.StackTrace });
        }
        catch
        {
            // Crash logging must never cause a second crash.
        }
    }

    private static void ShowControlledError()
    {
        MessageBox.Show(
            "حدث خطأ غير متوقع وتم تسجيله داخل D7. سيبقى البرنامج مفتوحًا إذا كان الاستمرار آمنًا.",
            "D7 BLACKCORE — خطأ",
            MessageBoxButton.OK,
            MessageBoxImage.Error,
            MessageBoxResult.OK,
            MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
    }
}
