using D7SystemIntelligence.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace D7SystemIntelligence;

public sealed class PerformanceContractWindow : Window
{
    private readonly PerformanceContractService _service;
    private readonly PerformanceContractSettingsStore _store;
    private readonly CheckBox _enabled = new();
    private readonly TextBox _fps = new();
    private readonly TextBox _low = new();
    private readonly TextBox _p99 = new();
    private readonly TextBox _cpuTemp = new();
    private readonly TextBox _gpuTemp = new();
    private readonly TextBox _ram = new();
    private readonly TextBox _ping = new();
    private readonly CheckBox _memoryClean = new();
    private readonly CheckBox _fans = new();
    private readonly CheckBox _captureGuard = new();
    private readonly TextBlock _status = new();

    public PerformanceContractWindow(PerformanceContractService service, PerformanceContractSettingsStore store)
    {
        _service = service; _store = store;
        Title = "D7 NEXUS • Performance Contract";
        Width = 820; Height = 700; MinWidth = 740; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.FindResource("Bg");
        Content = BuildUi();
        LoadSettings();
        _service.StatusChanged += OnStatus;
        Closed += (_, _) => _service.StatusChanged -= OnStatus;
        _status.Text = _service.LastStatus;
    }

    private UIElement BuildUi()
    {
        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "PERFORMANCE CONTRACT", FontSize = 30, FontWeight = FontWeights.Bold });
        header.Children.Add(new TextBlock
        {
            Text = "D7 يستخدم نفس Telemetry الخاصة بالجلسة — بدون PresentMon ثاني. إذا خالف الجهاز العقد، ينفذ فقط إجراءات آمنة ومحددة ثم يوضح ماذا فعل.",
            Foreground = (Brush)Application.Current.FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12)
        });
        Grid.SetRow(header, 0); root.Children.Add(header);

        var card = new Border { Background = (Brush)Application.Current.FindResource("Panel"), BorderBrush = (Brush)Application.Current.FindResource("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(16) };
        var panel = new StackPanel();
        _enabled.Content = "تفعيل العقد تلقائيًا مع جلسات اللعب"; _enabled.FontSize = 16; panel.Children.Add(_enabled);
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 12, 0, 8) };
        grid.Children.Add(Field("Target FPS", _fps));
        grid.Children.Add(Field("Target 1% Low", _low));
        grid.Children.Add(Field("Max P99 (ms)", _p99));
        grid.Children.Add(Field("Max CPU Temp °C", _cpuTemp));
        grid.Children.Add(Field("Max GPU Temp °C", _gpuTemp));
        grid.Children.Add(Field("Max RAM %", _ram));
        grid.Children.Add(Field("Max Ping ms", _ping));
        panel.Children.Add(grid);
        _memoryClean.Content = "RAM Guard: Smart Clean للعمليات Safe-To-Close فقط";
        _fans.Content = "Thermal Guard: تشغيل Smart Fans إذا توجد قنوات writable";
        _captureGuard.Content = "Capture Guard: أولوية FPS — إيقاف Shadow Capture عند هبوط قوي مستمر مع GPU saturation";
        panel.Children.Add(_memoryClean); panel.Children.Add(_fans); panel.Children.Add(_captureGuard);
        card.Child = panel; Grid.SetRow(card, 1); root.Children.Add(card);

        var statusCard = new Border { Background = (Brush)Application.Current.FindResource("AccentSoft"), BorderBrush = (Brush)Application.Current.FindResource("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(14), Margin = new Thickness(0, 14, 0, 0), Child = _status };
        _status.TextWrapping = TextWrapping.Wrap; _status.FontSize = 14;
        Grid.SetRow(statusCard, 2); root.Children.Add(statusCard);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var stop = new Button { Content = "إيقاف العقد", MinWidth = 110 };
        stop.Click += (_, _) => { _service.Stop(true); _enabled.IsChecked = false; Save(false); _status.Text = "Performance Contract متوقف وتمت استعادة Fan override الخاص به إن وُجد."; };
        var save = new Button { Content = "حفظ وتشغيل", MinWidth = 125, Background = (Brush)Application.Current.FindResource("AccentSoft") };
        save.Click += (_, _) => { var s = Read(); _store.Save(s); _service.Start(s); LoadSettings(); };
        actions.Children.Add(stop); actions.Children.Add(save); Grid.SetRow(actions, 3); root.Children.Add(actions);
        return root;
    }

    private Border Field(string label, TextBox box)
    {
        var stack = new StackPanel { Margin = new Thickness(5) };
        stack.Children.Add(new TextBlock { Text = label, Foreground = (Brush)Application.Current.FindResource("Muted") });
        stack.Children.Add(box);
        return new Border { Child = stack };
    }

    private void LoadSettings()
    {
        var s = _store.Load();
        _enabled.IsChecked = s.Enabled; _fps.Text = s.TargetFps.ToString(); _low.Text = s.TargetOnePercentLow.ToString(); _p99.Text = s.MaxP99FrameMs.ToString("0.#");
        _cpuTemp.Text = s.MaxCpuTemp.ToString("0.#"); _gpuTemp.Text = s.MaxGpuTemp.ToString("0.#"); _ram.Text = s.MaxRamLoad.ToString("0.#"); _ping.Text = s.MaxPingMs.ToString("0.#");
        _memoryClean.IsChecked = s.AutoSafeMemoryClean; _fans.IsChecked = s.AutoSmartFans; _captureGuard.IsChecked = s.ProtectFpsOverCapture;
    }

    private PerformanceContractSettings Read()
    {
        static int I(TextBox box, int fallback) => int.TryParse(box.Text.Trim(), out var v) ? v : fallback;
        static double D(TextBox box, double fallback) => double.TryParse(box.Text.Trim(), out var v) ? v : fallback;
        return new PerformanceContractSettings
        {
            Enabled = _enabled.IsChecked == true, TargetFps = I(_fps, 144), TargetOnePercentLow = I(_low, 100), MaxP99FrameMs = D(_p99, 20),
            MaxCpuTemp = D(_cpuTemp, 85), MaxGpuTemp = D(_gpuTemp, 82), MaxRamLoad = D(_ram, 90), MaxPingMs = D(_ping, 80),
            AutoSafeMemoryClean = _memoryClean.IsChecked == true, AutoSmartFans = _fans.IsChecked == true, ProtectFpsOverCapture = _captureGuard.IsChecked == true
        };
    }

    private void Save(bool enabled)
    {
        var s = Read(); s.Enabled = enabled; _store.Save(s);
    }

    private void OnStatus(string text) => Dispatcher.Invoke(() => _status.Text = text);
}
