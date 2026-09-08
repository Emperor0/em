using System.Windows;
using D7.App.Services;
using D7.Core.Logging;

namespace D7.App;

public partial class MainWindow : Window
{
    private readonly BootstrapService _bootstrap;
    private readonly JsonLineLogger _logger;
    private readonly bool _safeMode;
    private readonly CancellationTokenSource _lifetime = new();

    public MainWindow(BootstrapService bootstrap, JsonLineLogger logger, bool safeMode)
    {
        InitializeComponent();
        _bootstrap = bootstrap;
        _logger = logger;
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
                : "جارٍ التحقق من النظام والمتطلبات الأساسية...";

            var result = await _bootstrap.RunAsync(_safeMode, _lifetime.Token);
            ChecksList.ItemsSource = result.Checks.Select(x => new
            {
                Icon = x.Passed ? "✓" : x.Blocking ? "✕" : "!",
                Title = x.TitleAr,
                Detail = x.DetailAr
            }).ToArray();

            if (result.Ready)
            {
                HealthText.Text = _safeMode ? "وضع الأمان" : "جاهز للمرحلة التالية";
                SubtitleText.Text = _safeMode
                    ? "تم تشغيل الأساس الآمن بنجاح."
                    : "تم تجهيز الأساس بنجاح. سيبقى زر التحسين مغلقًا حتى يكتمل محرك القياس والتراجع في فرع التطوير.";
            }
            else
            {
                HealthText.Text = "يحتاج إصلاح";
                SubtitleText.Text = "هناك متطلب أساسي يمنع المتابعة. راجع نتيجة التجهيز أدناه.";
            }

            // Do not expose a fake optimization button. It remains disabled until the real
            // measurement + transaction + rollback core is integrated and tested.
            OptimizeButton.IsEnabled = false;
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

    private void OnClosed(object? sender, EventArgs e)
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
