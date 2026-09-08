using System.Windows;
using D7.App.Services;
using D7.Benchmark.Models;
using D7.Core.Logging;
using D7.Diagnostics;
using D7.Hardware.Discovery;
using D7.Hardware.Models;
using D7.Hardware.Telemetry;
using D7.Orchestration;
using D7.Orchestration.Planning;

namespace D7.App;

public partial class MainWindow : Window
{
    private readonly BootstrapService _bootstrap;
    private readonly JsonLineLogger _logger;
    private readonly WindowsHardwareDiscoveryService _hardwareDiscovery;
    private readonly SystemTelemetrySampler _telemetry;
    private readonly D7SelfOverheadGuard _overheadGuard = new();
    private readonly CoreFlowCoordinator _coreFlow;
    private readonly OptimizationPlanner _planner;
    private readonly StartupRecoveryService _recovery;
    private readonly DiagnosticsPackageService _diagnostics;
    private readonly bool _safeMode;
    private readonly CancellationTokenSource _lifetime = new();
    private HardwareSnapshot? _latestHardware;
    private bool _baselinePassed;
    private bool _busy;

    public MainWindow(
        BootstrapService bootstrap,
        JsonLineLogger logger,
        WindowsHardwareDiscoveryService hardwareDiscovery,
        SystemTelemetrySampler telemetry,
        CoreFlowCoordinator coreFlow,
        OptimizationPlanner planner,
        StartupRecoveryService recovery,
        DiagnosticsPackageService diagnostics,
        bool safeMode)
    {
        InitializeComponent();
        _bootstrap = bootstrap;
        _logger = logger;
        _hardwareDiscovery = hardwareDiscovery;
        _telemetry = telemetry;
        _coreFlow = coreFlow;
        _planner = planner;
        _recovery = recovery;
        _diagnostics = diagnostics;
        _safeMode = safeMode;
        Loaded += OnLoaded;
        Closed += OnClosed;
        DiagnosticsButton.Click += OnDiagnosticsClicked;
        MeasureButton.Click += OnMeasureClicked;
        OptimizeButton.Click += OnOptimizeClicked;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            DiagnosticsButton.IsEnabled = false;
            MeasureButton.IsEnabled = false;
            OptimizeButton.IsEnabled = false;
            SubtitleText.Text = "جارٍ فحص سجل التراجع السابق...";

            var recovery = await _recovery.RecoverAsync(_lifetime.Token);
            if (recovery.TransactionsFound > 0)
            {
                ExperimentText.Text = recovery.MessageAr;
                HealthText.Text = recovery.OperationsUnresolved == 0 ? "تمت الاستعادة" : "استعادة جزئية";
            }

            SubtitleText.Text = _safeMode
                ? "يعمل D7 في وضع الأمان. لن يتم تنفيذ تحسينات تلقائية."
                : "جارٍ التحقق من النظام واكتشاف العتاد...";

            var bootstrapTask = _bootstrap.RunAsync(_safeMode, _lifetime.Token);
            var hardwareTask = _hardwareDiscovery.CaptureAsync(_lifetime.Token);
            await Task.WhenAll(bootstrapTask, hardwareTask);

            var result = await bootstrapTask;
            var hardware = await hardwareTask;
            _latestHardware = hardware;
            DiagnosticsButton.IsEnabled = true;

            ChecksList.ItemsSource = result.Checks.Select(x => new
            {
                Icon = x.Passed ? "✓" : x.Blocking ? "✕" : "!",
                Title = x.TitleAr,
                Detail = x.DetailAr
            }).ToArray();

            CpuText.Text = hardware.Cpu.Name;
            MemoryText.Text = $"{hardware.Memory.TotalGiB:0.##} GB";
            var primaryGpu = hardware.DisplayAdapters.FirstOrDefault(x => x.Primary && !x.MirroringDriver)
                ?? hardware.DisplayAdapters.FirstOrDefault(x => x.AttachedToDesktop && !x.MirroringDriver)
                ?? hardware.DisplayAdapters.FirstOrDefault(x => !x.MirroringDriver);
            GpuText.Text = primaryGpu?.Name ?? "لم يتم التعرف على كرت الشاشة";

            await _logger.WriteAsync(
                "Hardware",
                "STARTUP-SCAN",
                "Information",
                "اكتمل فحص العتاد الأساسي.",
                new
                {
                    hardware.CapturedAt,
                    Cpu = hardware.Cpu.Name,
                    MemoryGiB = hardware.Memory.TotalGiB,
                    Board = $"{hardware.Board.Manufacturer} {hardware.Board.Product}",
                    hardware.Board.BiosVersion,
                    DisplayAdapters = hardware.DisplayAdapters.Select(x => x.Name).ToArray(),
                    StorageCount = hardware.Storage.Count,
                    NetworkCount = hardware.NetworkAdapters.Count,
                    StartupCount = hardware.StartupEntries.Count
                },
                _lifetime.Token);

            if (result.Ready && recovery.OperationsUnresolved == 0)
            {
                HealthText.Text = _safeMode ? "وضع الأمان" : "جاهز للقياس";
                SubtitleText.Text = _safeMode
                    ? "تم فحص الجهاز. القياس متاح، بينما التعديلات معطلة في وضع الأمان."
                    : $"تم التعرف على الجهاز: {hardware.Board.Manufacturer} {hardware.Board.Product}. ابدأ بقياس اللعبة قبل أي تجربة.";
                MeasureButton.IsEnabled = true;
            }
            else if (recovery.OperationsUnresolved > 0)
            {
                HealthText.Text = "استعادة مطلوبة";
                SubtitleText.Text = "توجد عملية سابقة لم يتمكن D7 من استعادتها تلقائيًا. القياس والتعديلات مقفلة حتى تتم مراجعتها.";
                MeasureButton.IsEnabled = false;
            }
            else
            {
                HealthText.Text = "يحتاج إصلاح";
                SubtitleText.Text = "هناك متطلب أساسي يمنع المتابعة. راجع نتيجة التجهيز أدناه.";
                MeasureButton.IsEnabled = false;
            }

            OptimizeButton.IsEnabled = false;
            _ = RunTelemetryLoopAsync(_lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            // Normal during shutdown.
        }
        catch (Exception ex)
        {
            await _logger.WriteAsync(
                "UI",
                Guid.NewGuid().ToString("N"),
                "Error",
                "فشل تجهيز الواجهة",
                new { ex.Message, ex.StackTrace });
            HealthText.Text = "خطأ مسجل";
            SubtitleText.Text = "تعذر إكمال التجهيز، وتم تسجيل التفاصيل دون إغلاق D7.";
        }
    }

    private async void OnDiagnosticsClicked(object sender, RoutedEventArgs e)
    {
        if (_busy || _latestHardware is null || _lifetime.IsCancellationRequested) return;
        _busy = true;
        SetActionButtonsEnabled(false);

        try
        {
            HealthText.Text = "إنشاء تقرير التشخيص";
            ExperimentText.Text = "يجمع D7 فقط سجلاته وملخص النظام وسجل العمليات وملفات القياس الوصفية، بدون ملفاتك الشخصية.";
            var result = await _diagnostics.CreateAsync(_latestHardware, _safeMode, _lifetime.Token);
            HealthText.Text = result.Success ? "تم إنشاء التقرير" : "تعذر إنشاء التقرير";
            ExperimentText.Text = result.Success && !string.IsNullOrWhiteSpace(result.ZipPath)
                ? $"تم حفظ تقرير التشخيص هنا: {result.ZipPath}"
                : result.MessageAr;
        }
        catch (OperationCanceledException)
        {
            // Normal during shutdown.
        }
        finally
        {
            _busy = false;
            RestoreActionButtons();
        }
    }

    private async void OnMeasureClicked(object sender, RoutedEventArgs e)
    {
        if (_busy || _lifetime.IsCancellationRequested) return;
        _busy = true;
        SetActionButtonsEnabled(false);

        try
        {
            HealthText.Text = "استعد للقياس";
            ExperimentText.Text = "ارجع إلى اللعبة الآن. سيبدأ القياس بعد 5 ثوانٍ ويستمر 20 ثانية.";
            await Task.Delay(TimeSpan.FromSeconds(5), _lifetime.Token);

            HealthText.Text = "جاري قياس اللعبة";
            var result = await _coreFlow.CaptureBaselineAsync(TimeSpan.FromSeconds(20), _lifetime.Token);
            if (!result.Success || result.Baseline?.Analysis is null)
            {
                _baselinePassed = false;
                FrameText.Text = result.Game is null ? "لم يتم اكتشاف لعبة" : result.Game.ProcessName;
                FrameDetailText.Text = result.Baseline?.MessageAr ?? result.MessageAr;
                ExperimentText.Text = result.MessageAr;
                HealthText.Text = "القياس غير مكتمل";
                return;
            }

            _baselinePassed = true;
            ShowFrameAnalysis(result.Game?.ProcessName ?? "اللعبة", result.Baseline.Analysis);
            ExperimentText.Text = "تم حفظ خط الأساس. D7 سيختار الآن فقط تجربة منخفضة المخاطر تنطبق فعلًا على جهازك.";
            HealthText.Text = "خط الأساس جاهز";
        }
        catch (OperationCanceledException)
        {
            // Window shutdown.
        }
        catch (Exception ex)
        {
            _baselinePassed = false;
            await _logger.WriteAsync(
                "UI",
                "MEASURE",
                "Error",
                "فشل قياس اللعبة من الواجهة.",
                new { ex.Message, ex.StackTrace },
                CancellationToken.None);
            HealthText.Text = "فشل القياس";
            ExperimentText.Text = "لم يطبق D7 أي تعديل.";
        }
        finally
        {
            _busy = false;
            RestoreActionButtons();
        }
    }

    private async void OnOptimizeClicked(object sender, RoutedEventArgs e)
    {
        if (_busy || _safeMode || !_baselinePassed || _lifetime.IsCancellationRequested) return;
        _busy = true;
        SetActionButtonsEnabled(false);

        try
        {
            HealthText.Text = "تحليل التجربة المناسبة";
            ExperimentText.Text = "ارجع إلى اللعبة الآن. بعد 5 ثوانٍ سيختار D7 تعديلًا منخفض المخاطر ينطبق فعلًا ثم يبدأ A/B.";
            await Task.Delay(TimeSpan.FromSeconds(5), _lifetime.Token);

            var game = await _coreFlow.DetectGameAsync(_lifetime.Token);
            if (game is null)
            {
                HealthText.Text = "لم يتم اكتشاف لعبة";
                ExperimentText.Text = "لم يغير D7 أي إعداد. شغّل اللعبة واتركها مفتوحة ثم أعد المحاولة.";
                return;
            }

            var plan = await _planner.SelectNextAsync(game, _lifetime.Token);
            await _logger.WriteAsync(
                "Planner",
                "AUTO",
                "Information",
                plan.MessageAr,
                new
                {
                    Game = game.ProcessName,
                    SelectedOperation = plan.Operation?.Id,
                    plan.Checks
                },
                CancellationToken.None);

            if (!plan.HasOperation || plan.Operation is null)
            {
                HealthText.Text = "لا يوجد تعديل مفيد الآن";
                ExperimentText.Text = plan.MessageAr;
                var lastCheck = plan.Checks.LastOrDefault();
                if (lastCheck is not null) FrameDetailText.Text = lastCheck.MessageAr;
                return;
            }

            HealthText.Text = "تجربة A/B";
            ExperimentText.Text = $"التجربة المختارة: {plan.Operation.NameAr}. سيقيس D7 الحالة الأصلية ثم التعديل ويطلب تأكيدًا إذا ظهر تحسن.";

            var result = await _coreFlow.RunExperimentAsync(plan.Operation, TimeSpan.FromSeconds(20), _lifetime.Token);
            ExperimentText.Text = result.MessageAr;

            var analysis = result.Confirmation?.Analysis ?? result.Candidate?.Analysis ?? result.Baseline?.Analysis;
            if (analysis is not null)
                ShowFrameAnalysis(result.Game?.ProcessName ?? "اللعبة", analysis);

            var finalComparison = result.ConfirmationComparison ?? result.Comparison;
            if (finalComparison is not null)
            {
                FrameDetailText.Text = $"أقل 1%: {finalComparison.OnePercentLowDeltaPercent:+0.0;-0.0;0.0}% | P99: {finalComparison.P99DeltaPercent:+0.0;-0.0;0.0}% | فرق التقطيع: {finalComparison.StutterDelta:+#;-#;0}";
            }

            HealthText.Text = result.RolledBack
                ? "تم التراجع تلقائيًا"
                : finalComparison?.Verdict switch
                {
                    BenchmarkVerdict.Keep => "تم اعتماد التحسن",
                    BenchmarkVerdict.Rollback => "تم التراجع تلقائيًا",
                    BenchmarkVerdict.Inconclusive => "النتيجة غير حاسمة",
                    _ => result.Success ? "اكتملت التجربة" : "لم يعتمد أي تعديل"
                };
        }
        catch (OperationCanceledException)
        {
            // CoreFlow attempts rollback before propagating cancellation.
        }
        catch (Exception ex)
        {
            await _logger.WriteAsync(
                "UI",
                "EXPERIMENT",
                "Error",
                "فشل تشغيل تجربة A/B من الواجهة.",
                new { ex.Message, ex.StackTrace },
                CancellationToken.None);
            HealthText.Text = "فشل آمن";
            ExperimentText.Text = "لم يعتمد D7 التعديل وتم تسجيل الخطأ.";
        }
        finally
        {
            _busy = false;
            RestoreActionButtons();
        }
    }

    private void SetActionButtonsEnabled(bool enabled)
    {
        DiagnosticsButton.IsEnabled = enabled && _latestHardware is not null;
        MeasureButton.IsEnabled = enabled;
        OptimizeButton.IsEnabled = enabled && _baselinePassed && !_safeMode;
    }

    private void RestoreActionButtons()
    {
        if (_lifetime.IsCancellationRequested) return;
        DiagnosticsButton.IsEnabled = _latestHardware is not null;
        MeasureButton.IsEnabled = true;
        OptimizeButton.IsEnabled = _baselinePassed && !_safeMode;
    }

    private void ShowFrameAnalysis(string gameName, FrameAnalysis analysis)
    {
        FrameText.Text = $"{gameName} | المتوسط {analysis.AverageFps:0.0} FPS | أقل 1% {analysis.OnePercentLowAverageFps:0.0} FPS";
        FrameDetailText.Text = $"P99 {analysis.P99FrameTimeMs:0.00} ms | التقطعات {analysis.StutterCount} | الإطارات {analysis.FrameCount}";
    }

    private async Task RunTelemetryLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_busy)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }

                var sample = await _telemetry.SampleAsync(cancellationToken);
                CpuUsageText.Text = sample.CpuUtilizationPercent is null
                    ? "الاستخدام: جارٍ المعايرة..."
                    : $"الاستخدام: {sample.CpuUtilizationPercent:0.0}%";
                MemoryUsageText.Text = $"الاستخدام: {sample.MemoryUsedPercent:0.0}% | المتاح {sample.MemoryAvailableGiB:0.00} GB";

                if (sample.Nvidia.Available)
                {
                    if (!string.IsNullOrWhiteSpace(sample.Nvidia.Name)) GpuText.Text = sample.Nvidia.Name;
                    var temperature = sample.Nvidia.TemperatureC is null ? "—" : $"{sample.Nvidia.TemperatureC:0}°C";
                    var usage = sample.Nvidia.UtilizationPercent is null ? "—" : $"{sample.Nvidia.UtilizationPercent:0}%";
                    GpuUsageText.Text = $"الاستخدام: {usage} | الحرارة {temperature}";
                }
                else
                {
                    GpuUsageText.Text = "القياس اللحظي غير متاح حاليًا";
                }

                var overhead = _overheadGuard.Observe(sample.D7CpuUtilizationPercent);
                if (overhead.StateChanged)
                {
                    await _logger.WriteAsync(
                        "PerformanceBudget",
                        "SELF-OVERHEAD",
                        overhead.ThrottleTelemetry ? "Warning" : "Information",
                        overhead.MessageAr,
                        new { sample.D7CpuUtilizationPercent, overhead.ThrottleTelemetry },
                        CancellationToken.None);
                }

                await Task.Delay(
                    overhead.ThrottleTelemetry ? TimeSpan.FromSeconds(3) : TimeSpan.FromSeconds(1),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                await _logger.WriteAsync(
                    "Telemetry",
                    "LIVE",
                    "Warning",
                    "تعذر تحديث القياسات اللحظية وسيعاد المحاولة.",
                    new { ex.Message },
                    CancellationToken.None);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
