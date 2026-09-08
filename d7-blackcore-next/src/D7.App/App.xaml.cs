using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using D7.App.Services;
using D7.Benchmark.Capture;
using D7.Core.Foundation;
using D7.Core.Logging;
using D7.Diagnostics;
using D7.Games.Detection;
using D7.Games.Profiles;
using D7.Hardware.Discovery;
using D7.Hardware.Telemetry;
using D7.Optimization.Operations;
using D7.Orchestration;
using D7.Orchestration.Learning;
using D7.Orchestration.Planning;
using D7.Rollback.Journal;
using D7.Stability;
using D7.Tools.Acquisition;

namespace D7.App;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private JsonLineLogger? _logger;
    private HttpClient? _bootstrapHttp;
    private HttpClient? _toolsHttp;

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

        _bootstrapHttp = new HttpClient();
        _toolsHttp = new HttpClient();

        var tools = new ToolAcquisitionService(paths, _logger, _toolsHttp);
        var bootstrap = new BootstrapService(paths, _logger, _bootstrapHttp, tools);
        var hardwareDiscovery = new WindowsHardwareDiscoveryService();
        var telemetry = new SystemTelemetrySampler();
        var diagnostics = new DiagnosticsPackageService(paths, _logger);

        var profiles = new GameProfileStore(paths);
        var gameDetector = new GameDetectionService(
            new ForegroundProcessReader(),
            new RunningProcessReader(),
            new GameProcessClassifier(),
            profiles);

        var frameCapture = new PresentMonCaptureService(paths, _logger, tools);
        var journal = new TransactionJournal(paths);
        var stability = new EventLogStabilityProbe();
        var coreFlow = new CoreFlowCoordinator(gameDetector, frameCapture, journal, _logger, stability);

        var powerPlanExperiment = new HighPerformancePowerPlanOptimization();
        var priorityExperiment = new ProcessPriorityOptimization();
        var automaticOperations = new D7.Optimization.Contracts.IReversibleOptimizationOperation[]
        {
            powerPlanExperiment,
            priorityExperiment
        };

        var history = new OptimizationHistoryStore(paths);
        var learning = new OptimizationLearningService(history);
        var planner = new OptimizationPlanner(automaticOperations, history);
        var recovery = new StartupRecoveryService(journal, automaticOperations, _logger);

        var window = new MainWindow(
            bootstrap,
            _logger,
            hardwareDiscovery,
            telemetry,
            coreFlow,
            planner,
            recovery,
            diagnostics,
            learning,
            safeMode);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _bootstrapHttp?.Dispose();
            _toolsHttp?.Dispose();
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
