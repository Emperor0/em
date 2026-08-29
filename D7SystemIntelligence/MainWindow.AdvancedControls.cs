using D7SystemIntelligence.Core;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace D7SystemIntelligence;

internal static class AdvancedFeatureBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => ((MainWindow)sender).InitializeAdvancedControls()),
            true);
    }
}

public partial class MainWindow
{
    private bool _advancedControlsInjected;
    private SmartFanController? _smartFans;
    private readonly NetworkGamingProfileService _networkGamingProfile = new();
    private TextBlock? _fanAutoStatus;

    private void InitializeAdvancedControls()
    {
        if (_advancedControlsInjected) return;
        _advancedControlsInjected = true;

        InjectAdvancedSidebarButtons();
        InjectPeripheralLabButton();
        InjectSmartFanControls();
        InjectNetworkGamingControls();

        Closed += (_, _) =>
        {
            try { _smartFans?.Dispose(); } catch { }
        };
    }

    private void InjectAdvancedSidebarButtons()
    {
        var sidebar = FindVisualChildren<StackPanel>(this)
            .FirstOrDefault(stack => stack.Children.OfType<Button>()
                .Any(button => string.Equals(button.Content?.ToString(), "التحديثات والإصلاح", StringComparison.Ordinal)));
        if (sidebar == null) return;

        var update = sidebar.Children.OfType<Button>()
            .First(button => string.Equals(button.Content?.ToString(), "التحديثات والإصلاح", StringComparison.Ordinal));
        var insertIndex = sidebar.Children.IndexOf(update);

        if (!sidebar.Children.OfType<Button>().Any(x => Equals(x.Content, "مختبر الإدخال")))
        {
            var input = new Button { Content = "مختبر الإدخال" };
            input.Click += (_, _) => new InputLabWindow { Owner = this }.ShowDialog();
            sidebar.Children.Insert(insertIndex++, input);
        }

        if (!sidebar.Children.OfType<Button>().Any(x => Equals(x.Content, "الشاشة والتحكم")))
        {
            var display = new Button { Content = "الشاشة والتحكم" };
            display.Click += (_, _) => new DisplayControlWindow { Owner = this }.ShowDialog();
            sidebar.Children.Insert(insertIndex++, display);
        }

        if (!sidebar.Children.OfType<Button>().Any(x => Equals(x.Content, "RGB Studio")))
        {
            var rgb = new Button { Content = "RGB Studio" };
            rgb.Click += (_, _) => new RgbStudioWindow(_hardware) { Owner = this }.ShowDialog();
            sidebar.Children.Insert(insertIndex, rgb);
        }
    }

    private void InjectPeripheralLabButton()
    {
        var scan = PeripheralsPage.Children.OfType<Button>()
            .FirstOrDefault(x => string.Equals(x.Content?.ToString(), "فحص الأجهزة الطرفية", StringComparison.Ordinal));
        if (scan == null) return;

        PeripheralsPage.Children.Remove(scan);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 16) };
        row.Children.Add(scan);
        var lab = new Button { Content = "مختبر Polling / Drift" };
        lab.Click += (_, _) => new InputLabWindow { Owner = this }.ShowDialog();
        row.Children.Add(lab);
        Grid.SetRow(row, 1);
        PeripheralsPage.Children.Add(row);
    }

    private void InjectSmartFanControls()
    {
        _smartFans = new SmartFanController(_hardware);
        _smartFans.StatusChanged += message => Dispatcher.Invoke(() =>
        {
            if (_fanAutoStatus != null) _fanAutoStatus.Text = message;
            StatusText.Text = message;
        });

        var existingRow = FansPage.Children.OfType<StackPanel>()
            .FirstOrDefault(x => x.Children.OfType<Button>().Any(b => string.Equals(b.Content?.ToString(), "استعادة BIOS / AUTO", StringComparison.Ordinal)));
        if (existingRow == null) return;

        _fanAutoStatus = new TextBlock
        {
            Text = "AUTO Fan متوقف. سيعمل فقط إذا ظهرت قناة writable حقيقية.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var start = new Button { Content = "تشغيل AUTO ذكي" };
        start.Click += (_, _) =>
        {
            if (_smartFans.Start()) RefreshHardware();
        };
        var stop = new Button { Content = "إيقاف AUTO + استعادة" };
        stop.Click += (_, _) =>
        {
            _smartFans.Stop(true);
            RefreshHardware();
        };

        existingRow.Children.Insert(0, start);
        existingRow.Children.Insert(1, stop);
        existingRow.Children.Add(_fanAutoStatus);
    }

    private void InjectNetworkGamingControls()
    {
        var border = NetworkNotesText.Parent as Border;
        if (border == null) return;

        border.Child = null;
        var stack = new StackPanel();
        stack.Children.Add(NetworkNotesText);
        var warning = new TextBlock
        {
            Text = "Gaming Network يغيّر فقط خصائص توفير الطاقة المعروفة ويأخذ Restore Backup. قد ينقطع الاتصال عدة ثوانٍ عند إعادة تشغيل المحول.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = .72,
            Margin = new Thickness(0, 10, 0, 8)
        };
        stack.Children.Add(warning);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var apply = new Button { Content = "تطبيق Gaming Network" };
        var restore = new Button { Content = "استعادة إعدادات الشبكة" };

        apply.Click += async (_, _) =>
        {
            apply.IsEnabled = false;
            restore.IsEnabled = false;
            try
            {
                NetworkNotesText.Text = "جاري أخذ قياس قبل التعديل…";
                var before = await _network.ScanAsync();
                NetworkNotesText.Text = "جاري حفظ النسخة الأصلية وتطبيق خصائص الشبكة الآمنة…";
                var result = await _networkGamingProfile.ApplyAsync();
                if (!result.Success)
                {
                    NetworkNotesText.Text = result.Detail;
                    return;
                }
                NetworkNotesText.Text = result.Detail + "\n\nجاري انتظار عودة الاتصال ثم القياس مرة أخرى…";
                await Task.Delay(TimeSpan.FromSeconds(7));
                var after = await _network.ScanAsync();
                NetworkNotesText.Text = result.Detail + "\n\n" + CompareNetwork(before, after);
            }
            catch (Exception ex)
            {
                NetworkNotesText.Text = "Gaming Network: " + ex.Message;
            }
            finally
            {
                apply.IsEnabled = true;
                restore.IsEnabled = true;
            }
        };

        restore.Click += async (_, _) =>
        {
            apply.IsEnabled = false;
            restore.IsEnabled = false;
            try
            {
                NetworkNotesText.Text = "جاري استعادة إعدادات NIC الأصلية…";
                var result = await _networkGamingProfile.RestoreAsync();
                await Task.Delay(TimeSpan.FromSeconds(6));
                NetworkNotesText.Text = result.Detail;
                await ScanNetworkCore();
            }
            catch (Exception ex) { NetworkNotesText.Text = "استعادة الشبكة: " + ex.Message; }
            finally { apply.IsEnabled = true; restore.IsEnabled = true; }
        };

        buttons.Children.Add(apply);
        buttons.Children.Add(restore);
        stack.Children.Add(buttons);
        border.Child = stack;
    }

    private static string CompareNetwork(NetworkReport before, NetworkReport after)
    {
        static string M(double? v) => v.HasValue ? $"{v.Value:0.0}ms" : "—";
        var latencyDelta = before.InternetLatencyMs.HasValue && after.InternetLatencyMs.HasValue
            ? after.InternetLatencyMs.Value - before.InternetLatencyMs.Value
            : (double?)null;
        var jitterDelta = before.JitterMs.HasValue && after.JitterMs.HasValue
            ? after.JitterMs.Value - before.JitterMs.Value
            : (double?)null;

        var verdict = latencyDelta.HasValue
            ? latencyDelta.Value < -1 ? "تحسن القياس الحالي." : latencyDelta.Value > 2 ? "القياس الحالي أسوأ؛ يمكنك الضغط على الاستعادة." : "لا يوجد فرق مهم في Latency بهذا الاختبار."
            : "تعذر حساب فرق Latency.";

        return $"قبل: Ping {M(before.InternetLatencyMs)} | Jitter {M(before.JitterMs)} | Loss {before.PacketLossPercent:0.#}%\n" +
               $"بعد: Ping {M(after.InternetLatencyMs)} | Jitter {M(after.JitterMs)} | Loss {after.PacketLossPercent:0.#}%\n" +
               $"Δ Ping {(latencyDelta.HasValue ? latencyDelta.Value.ToString("+0.0;-0.0;0.0") + "ms" : "—")} | Δ Jitter {(jitterDelta.HasValue ? jitterDelta.Value.ToString("+0.0;-0.0;0.0") + "ms" : "—")}\n{verdict}";
    }
}
