using System.Windows;
using D7.App.Services;
using D7.Core.Logging;
using D7.Hardware.Discovery;
using D7.Hardware.Telemetry;

namespace D7.App;

public partial class MainWindow : Window
{
    private readonly BootstrapService _bootstrap;
    private readonly JsonLineLogger _logger;
    private readonly WindowsHardwareDiscoveryService _hardwareDiscovery;
    private readonly SystemTelemetrySampler _telemetry;
    private readonly bool _safeMode;
    private readonly CancellationTokenSource _lifetime = new();

    public MainWindow(
        BootstrapService bootstrap,
        JsonLineLogger logger,
        WindowsHardwareDiscoveryService hardwareDiscovery,
        SystemTelemetrySampler telemetry,
        bool safeMode)
    {
        InitializeComponent();
        _bootstrap = bootstrap;
        _logger = logger;
        _hardwareDiscovery = hardwareDiscovery;
        _telemetry = telemetry;
        _safeMode = safeMode;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            SubtitleText.Text = _safeMode
                ? "يعمل D7 في وضع الأمان. لن يتم تنفيذ تحسينات تلقائية."
                : "جارٍ التحقق من النظام واكتشاف العتاد...";

            var bootstrapTask = _bootstrap.RunAsync(_safeMode, _lifetime.Token);
            var hardwareTask = _hardwareDiscovery.CaptureAsync(_lifetime.Token);
            await Task.WhenAll(bootstrapTask, hardwareTask);

            var result = await bootstrapTask;
            var hardware = await hardwareTask;

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

            if (result.Ready)
            {
                HealthText.Text = _safeMode ? "وضع الأمان" : "الفحص الأساسي مكتمل";
                SubtitleText.Text = _safeMode
                    ? "تم تشغيل الأساس الآمن وفحص الجهاز بنجاح."
                    : $"تم التعرف على الجهاز: {hardware.Board.Manufacturer} {hardware.Board.Product}. القياس والتحسين سيبقيان مغلقين حتى يكتمل المسار الآمن بالكامل.";
            }
            else
            {
                HealthText.Text = "يحتاج إصلاح";
                SubtitleText.Text = "هناك متطلب أساسي يمنع المتابعة. راجع نتيجة التجهيز أدناه.";
            }

            // The button remains locked until game detection + measurement + transaction rollback
            // are integrated end-to-end. No fake production action is exposed during development.
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

    private async Task RunTelemetryLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
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

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
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
