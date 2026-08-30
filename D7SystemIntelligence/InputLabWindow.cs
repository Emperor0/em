using D7SystemIntelligence.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace D7SystemIntelligence;

public sealed class InputLabWindow : Window
{
    private readonly PhysicalPeripheralIntelligence _physical = new();
    private readonly MouseSystemTuningService _mouseTuning = new();
    private readonly DataGrid _devices = new();
    private readonly TextBlock _mouseResult = new();
    private readonly TextBlock _mouseWindowsState = new();
    private readonly TextBlock _controllerResult = new();
    private readonly TextBlock _controllerRange = new();
    private readonly TextBlock _keyboardState = new();
    private readonly TextBlock _inputScore = new();
    private readonly List<long> _mouseTicks = [];
    private readonly HashSet<Key> _keysDown = [];
    private HwndSource? _source;
    private bool _measuringMouse;
    private int _maxSimultaneousKeys;
    private double? _lastMouseScore;
    private double? _lastControllerScore;

    public InputLabWindow()
    {
        Title = "D7KT — Input Intelligence Lab";
        Width = 1220;
        Height = 820;
        MinWidth = 980;
        MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("Bg", Brushes.Black);
        Foreground = Brush("Text", Brushes.White);

        Content = BuildUi();
        SourceInitialized += OnSourceInitialized;
        Loaded += async (_, _) =>
        {
            await RefreshDevicesAsync();
            RefreshWindowsMouseState();
        };
        PreviewKeyDown += OnKeyDown;
        PreviewKeyUp += OnKeyUp;
        Deactivated += (_, _) =>
        {
            _keysDown.Clear();
            UpdateKeyboardState();
        };
        Closed += (_, _) => _source?.RemoveHook(WndProc);
    }

    private UIElement BuildUi()
    {
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = "Input Intelligence Lab", FontSize = 30, FontWeight = FontWeights.Bold });
        root.Children.Add(new TextBlock
        {
            Text = "قياس فعلي مستقل عن الشركة: Raw Input polling، jitter/stalls، Windows mouse path، NKRO، Controller drift/range. D7KT لا يدعي تغيير DPI أو firmware بدون بروتوكول الجهاز نفسه.",
            Foreground = Brush("Muted", Brushes.Gray), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12)
        });

        var scoreCard = Card();
        var scoreStack = new StackPanel();
        scoreStack.Children.Add(new TextBlock { Text = "Input Health Score", FontSize = 18, FontWeight = FontWeights.SemiBold });
        _inputScore.Text = "شغّل Mouse Polling وController Drift حتى يحسب D7KT تقييمًا مبنيًا على قياسات فعلية.";
        _inputScore.Foreground = Brush("Muted", Brushes.Gray);
        _inputScore.TextWrapping = TextWrapping.Wrap;
        _inputScore.Margin = new Thickness(0, 6, 0, 0);
        scoreStack.Children.Add(_inputScore);
        scoreCard.Child = scoreStack;
        root.Children.Add(scoreCard);

        var devicesCard = Card();
        var ds = new StackPanel();
        var dh = new Grid();
        dh.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dh.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        dh.Children.Add(new TextBlock { Text = "Physical Device Map", FontSize = 19, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var scan = new Button { Content = "إعادة فحص الأجهزة" };
        scan.Click += async (_, _) => await RefreshDevicesAsync();
        Grid.SetColumn(scan, 1);
        dh.Children.Add(scan);
        ds.Children.Add(dh);
        ds.Children.Add(new TextBlock
        {
            Text = "يجمع واجهات HID التابعة لنفس الجهاز باستخدام Container/Parent IDs ويعرض Transport وUSB Location بدل تكرار عشر واجهات لنفس الماوس.",
            Foreground = Brush("Muted", Brushes.Gray), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 8)
        });
        ConfigureDeviceGrid();
        ds.Children.Add(_devices);
        devicesCard.Child = ds;
        root.Children.Add(devicesCard);

        var row1 = new UniformGrid { Columns = 2, Margin = new Thickness(-4, 4, -4, 4) };
        row1.Children.Add(BuildMousePollingPanel());
        row1.Children.Add(BuildWindowsMousePanel());
        root.Children.Add(row1);

        var row2 = new UniformGrid { Columns = 2, Margin = new Thickness(-4, 4, -4, 4) };
        row2.Children.Add(BuildKeyboardPanel());
        row2.Children.Add(BuildControllerPanel());
        root.Children.Add(row2);

        var truth = Card();
        truth.Child = new TextBlock
        {
            Text = "حدود القياس: D7KT يستطيع قياس وصول Raw Input إلى Windows، لكنه لا يستطيع قياس click‑to‑photon الحقيقي بدون جهاز خارجي عالي السرعة. كذلك DPI/CPI وLift‑off وDebounce وOnboard Memory تحتاج Adapter خاص بكل Vendor/Device؛ لن تظهر أزرار وهمية لها.",
            Foreground = Brush("Muted", Brushes.Gray), TextWrapping = TextWrapping.Wrap
        };
        root.Children.Add(truth);

        return new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    private void ConfigureDeviceGrid()
    {
        _devices.AutoGenerateColumns = false;
        _devices.IsReadOnly = true;
        _devices.Height = 230;
        _devices.Columns.Add(new DataGridTextColumn { Header = "الفئة", Binding = new System.Windows.Data.Binding(nameof(PhysicalPeripheralRecord.Category)), Width = new DataGridLength(.7, DataGridLengthUnitType.Star) });
        _devices.Columns.Add(new DataGridTextColumn { Header = "الجهاز", Binding = new System.Windows.Data.Binding(nameof(PhysicalPeripheralRecord.Name)), Width = new DataGridLength(2.0, DataGridLengthUnitType.Star) });
        _devices.Columns.Add(new DataGridTextColumn { Header = "الاتصال", Binding = new System.Windows.Data.Binding(nameof(PhysicalPeripheralRecord.Transport)), Width = new DataGridLength(.75, DataGridLengthUnitType.Star) });
        _devices.Columns.Add(new DataGridTextColumn { Header = "واجهات", Binding = new System.Windows.Data.Binding(nameof(PhysicalPeripheralRecord.InterfaceCount)), Width = new DataGridLength(.55, DataGridLengthUnitType.Star) });
        _devices.Columns.Add(new DataGridTextColumn { Header = "USB / Location", Binding = new System.Windows.Data.Binding(nameof(PhysicalPeripheralRecord.Location)), Width = new DataGridLength(1.8, DataGridLengthUnitType.Star) });
        _devices.Columns.Add(new DataGridTextColumn { Header = "الحالة", Binding = new System.Windows.Data.Binding(nameof(PhysicalPeripheralRecord.Status)), Width = new DataGridLength(.7, DataGridLengthUnitType.Star) });
    }

    private Border BuildMousePollingPanel()
    {
        var panel = Card();
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Mouse Polling Analyzer", FontSize = 20, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock
        {
            Text = "حرّك الماوس بسرعة وباستمرار 8 ثوانٍ. النتيجة ليست Hz فقط: Median/P95/P99/Jitter/Stalls/Stability Score.",
            Foreground = Brush("Muted", Brushes.Gray), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 8)
        });
        _mouseResult.Text = "لم يبدأ الاختبار بعد.";
        _mouseResult.TextWrapping = TextWrapping.Wrap;
        _mouseResult.Margin = new Thickness(0, 6, 0, 8);
        stack.Children.Add(_mouseResult);
        var button = new Button { Content = "ابدأ اختبار 8 ثوانٍ", HorizontalAlignment = HorizontalAlignment.Right };
        button.Click += (_, _) => StartMousePollingTest(button);
        stack.Children.Add(button);
        panel.Child = stack;
        return panel;
    }

    private Border BuildWindowsMousePanel()
    {
        var panel = Card();
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Windows Mouse Path", FontSize = 20, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock
        {
            Text = "قراءة Pointer Speed وEnhance Pointer Precision. Competitive baseline = 10/20 + acceleration OFF مع Backup واستعادة. Raw Input games قد تتجاوز هذه القيم.",
            Foreground = Brush("Muted", Brushes.Gray), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 8)
        });
        _mouseWindowsState.TextWrapping = TextWrapping.Wrap;
        _mouseWindowsState.Margin = new Thickness(0, 5, 0, 8);
        stack.Children.Add(_mouseWindowsState);
        var row = new WrapPanel();
        var refresh = new Button { Content = "قراءة الحالة" };
        refresh.Click += (_, _) => RefreshWindowsMouseState();
        var apply = new Button { Content = "Competitive Baseline" };
        apply.Click += (_, _) =>
        {
            try { _mouseWindowsState.Text = _mouseTuning.ApplyCompetitiveBaseline(); RefreshWindowsMouseState(); }
            catch (Exception ex) { _mouseWindowsState.Text = ex.Message; }
        };
        var restore = new Button { Content = "استعادة Backup" };
        restore.Click += (_, _) =>
        {
            try { _mouseWindowsState.Text = _mouseTuning.Restore(); RefreshWindowsMouseState(); }
            catch (Exception ex) { _mouseWindowsState.Text = ex.Message; }
        };
        row.Children.Add(refresh); row.Children.Add(apply); row.Children.Add(restore);
        stack.Children.Add(row);
        panel.Child = stack;
        return panel;
    }

    private Border BuildKeyboardPanel()
    {
        var panel = Card();
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Keyboard NKRO / Ghosting Tester", FontSize = 20, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock
        {
            Text = "اضغط عدة أزرار معًا داخل هذه النافذة. D7KT يعرض المفاتيح المسجلة وأعلى عدد متزامن وصل إلى Windows. هذا يختبر registration/rollover، وليس latency الميكانيكي للسويتش.",
            Foreground = Brush("Muted", Brushes.Gray), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 8)
        });
        _keyboardState.Text = "Current: — | Max simultaneous: 0";
        _keyboardState.TextWrapping = TextWrapping.Wrap;
        _keyboardState.Margin = new Thickness(0, 6, 0, 8);
        stack.Children.Add(_keyboardState);
        var clear = new Button { Content = "تصفير الاختبار", HorizontalAlignment = HorizontalAlignment.Right };
        clear.Click += (_, _) => { _maxSimultaneousKeys = 0; _keysDown.Clear(); UpdateKeyboardState(); Focus(); };
        stack.Children.Add(clear);
        panel.Child = stack;
        return panel;
    }

    private Border BuildControllerPanel()
    {
        var panel = Card();
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Controller Diagnostics", FontSize = 20, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock
        {
            Text = "Drift: اترك الأنالوج بدون لمس. Range: بعدها لف العصاتين دوائر كاملة وحرّك التريجرز للنهاية لقياس التغطية الخام.",
            Foreground = Brush("Muted", Brushes.Gray), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 8)
        });
        _controllerResult.Text = "Drift: لم يبدأ.";
        _controllerResult.TextWrapping = TextWrapping.Wrap;
        _controllerResult.Margin = new Thickness(0, 4, 0, 6);
        stack.Children.Add(_controllerResult);
        _controllerRange.Text = "Range: لم يبدأ.";
        _controllerRange.TextWrapping = TextWrapping.Wrap;
        _controllerRange.Margin = new Thickness(0, 4, 0, 8);
        stack.Children.Add(_controllerRange);

        var row = new WrapPanel();
        var drift = new Button { Content = "Drift Test • 6s" };
        drift.Click += async (_, _) =>
        {
            drift.IsEnabled = false;
            _controllerResult.Text = "لا تلمس الأنالوج… جاري القياس 6 ثوانٍ.";
            try
            {
                var r = await ControllerDiagnostics.MeasureDriftAsync(TimeSpan.FromSeconds(6));
                _controllerResult.Text = r.Text;
                _lastControllerScore = r.Score;
                UpdateOverallScore();
            }
            finally { drift.IsEnabled = true; }
        };
        var range = new Button { Content = "Stick Range • 8s" };
        range.Click += async (_, _) =>
        {
            range.IsEnabled = false;
            _controllerRange.Text = "الآن لف العصاتين دوائر كاملة واضغط LT/RT للنهاية…";
            try { _controllerRange.Text = await ControllerDiagnostics.MeasureRangeAsync(TimeSpan.FromSeconds(8)); }
            finally { range.IsEnabled = true; }
        };
        row.Children.Add(drift); row.Children.Add(range);
        stack.Children.Add(row);
        panel.Child = stack;
        return panel;
    }

    private async Task RefreshDevicesAsync()
    {
        try
        {
            var records = await _physical.ScanAsync();
            _devices.ItemsSource = records.Where(x => x.Category is "ماوس" or "كيبورد" or "يد تحكم" or "HID" or "بلوتوث").ToList();
        }
        catch (Exception ex)
        {
            _devices.ItemsSource = null;
            _mouseResult.Text = "Peripheral scan: " + ex.Message;
        }
    }

    private void RefreshWindowsMouseState()
    {
        try
        {
            var s = _mouseTuning.Read();
            _mouseWindowsState.Text = $"PointerSpeed {s.PointerSpeed}/20 • Enhance Pointer Precision {(s.EnhancePointerPrecision ? "ON" : "OFF")} • Thresholds {s.Threshold1}/{s.Threshold2} • Acceleration {s.Acceleration}.";
        }
        catch (Exception ex) { _mouseWindowsState.Text = ex.Message; }
    }

    private void StartMousePollingTest(Button button)
    {
        if (_source == null)
        {
            _mouseResult.Text = "تعذر إنشاء Raw Input hook لهذه النافذة.";
            return;
        }

        _mouseTicks.Clear();
        _measuringMouse = true;
        button.IsEnabled = false;
        _mouseResult.Text = "الآن حرّك الماوس بسرعة وباستمرار… لا توقف الحركة حتى نهاية 8 ثوانٍ.";

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _measuringMouse = false;
            button.IsEnabled = true;
            var result = BuildMouseResult();
            _mouseResult.Text = result.Text;
            _lastMouseScore = result.Score;
            UpdateOverallScore();
        };
        timer.Start();
    }

    private (string Text, double? Score) BuildMouseResult()
    {
        if (_mouseTicks.Count < 50)
            return ($"وصلت {_mouseTicks.Count} حزمة فقط. حرّك الماوس أكثر وبسرعة أثناء الاختبار.", null);

        var intervals = new List<double>(_mouseTicks.Count - 1);
        for (var i = 1; i < _mouseTicks.Count; i++)
            intervals.Add((_mouseTicks[i] - _mouseTicks[i - 1]) * 1000.0 / Stopwatch.Frequency);

        intervals = intervals.Where(x => x > 0.04 && x < 100).OrderBy(x => x).ToList();
        if (intervals.Count < 40) return ("العينات غير كافية لحساب Polling موثوق.", null);

        var median = Percentile(intervals, .50);
        var p95 = Percentile(intervals, .95);
        var p99 = Percentile(intervals, .99);
        var avg = intervals.Average();
        var jitter = Math.Sqrt(intervals.Sum(x => Math.Pow(x - avg, 2)) / intervals.Count);
        var hz = median <= 0 ? 0 : 1000.0 / median;
        var rounded = SnapPollingRate(hz);
        var expectedMs = 1000.0 / Math.Max(1, rounded);
        var stalls = intervals.Count(x => x > expectedMs * 2.5);
        var severeStalls = intervals.Count(x => x > Math.Max(8, expectedMs * 6));
        var deviation = Math.Abs(hz - rounded) / rounded;
        var jitterPenalty = Math.Min(45, jitter / Math.Max(.1, expectedMs) * 30);
        var stallPenalty = Math.Min(35, stalls * 100.0 / intervals.Count * 5);
        var ratePenalty = Math.Min(20, deviation * 100);
        var score = Math.Clamp(100 - jitterPenalty - stallPenalty - ratePenalty, 0, 100);

        var verdict = score >= 92 ? "ممتاز ومستقر"
            : score >= 80 ? "جيد"
            : score >= 65 ? "متوسط — راقب USB/CPU load"
            : "غير مستقر — اختبر منفذ USB آخر أو Polling أقل";

        var text = $"Samples {_mouseTicks.Count:N0} • Effective ≈ {hz:0}Hz → nearest {rounded}Hz\n" +
                   $"Median {median:0.###}ms • P95 {p95:0.###}ms • P99 {p99:0.###}ms • Jitter σ {jitter:0.###}ms\n" +
                   $"Stalls {stalls} • Severe {severeStalls} • Stability {score:0}/100 — {verdict}.";
        return (text, score);
    }

    private void UpdateOverallScore()
    {
        var parts = new List<double>();
        if (_lastMouseScore.HasValue) parts.Add(_lastMouseScore.Value);
        if (_lastControllerScore.HasValue) parts.Add(_lastControllerScore.Value);
        if (parts.Count == 0) return;
        var score = parts.Average();
        var label = score >= 92 ? "ممتاز" : score >= 80 ? "جيد جدًا" : score >= 65 ? "مقبول" : "يحتاج مراجعة";
        _inputScore.Text = $"{score:0}/100 • {label} • مبني فقط على الاختبارات التي شغلتها ({parts.Count}). لا يضيف D7KT نقاطًا لشيء لم يقسه.";
        _inputScore.Foreground = score >= 90 ? Brush("Success", Brushes.LightGreen) : score >= 70 ? Brush("Warning", Brushes.Gold) : Brush("Danger", Brushes.OrangeRed);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        _keysDown.Add(key);
        _maxSimultaneousKeys = Math.Max(_maxSimultaneousKeys, _keysDown.Count);
        UpdateKeyboardState();
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        _keysDown.Remove(key);
        UpdateKeyboardState();
    }

    private void UpdateKeyboardState()
    {
        var current = _keysDown.Count == 0 ? "—" : string.Join(" + ", _keysDown.OrderBy(x => x.ToString()).Select(x => x.ToString()));
        _keyboardState.Text = $"Current ({_keysDown.Count}): {current}\nMax simultaneous registered: {_maxSimultaneousKeys}.";
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);
        var devices = new[] { new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x02, dwFlags = 0x00000100, hwndTarget = hwnd } };
        RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_measuringMouse || msg != WM_INPUT) return IntPtr.Zero;
        uint size = 0;
        var headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        if (GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, headerSize) != 0 || size == 0) return IntPtr.Zero;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, RID_INPUT, buffer, ref size, headerSize) != size) return IntPtr.Zero;
            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buffer);
            if (header.dwType != RIM_TYPEMOUSE) return IntPtr.Zero;
            var mousePtr = IntPtr.Add(buffer, Marshal.SizeOf<RAWINPUTHEADER>());
            var mouse = Marshal.PtrToStructure<RAWMOUSE>(mousePtr);
            if (mouse.lLastX != 0 || mouse.lLastY != 0)
                _mouseTicks.Add(Stopwatch.GetTimestamp());
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return IntPtr.Zero;
    }

    private static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        var idx = Math.Clamp((int)Math.Round((sorted.Count - 1) * p), 0, sorted.Count - 1);
        return sorted[idx];
    }

    private static int SnapPollingRate(double hz)
    {
        var rates = new[] { 125, 250, 500, 1000, 2000, 4000, 8000 };
        return rates.OrderBy(x => Math.Abs(x - hz)).First();
    }

    private Border Card() => new()
    {
        Background = Brush("Panel", Brushes.DimGray),
        BorderBrush = Brush("Border", Brushes.Gray),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(15),
        Margin = new Thickness(4)
    };

    private static Brush Brush(string key, Brush fallback)
        => Application.Current.TryFindResource(key) as Brush ?? fallback;

    private const int WM_INPUT = 0x00FF;
    private const uint RID_INPUT = 0x10000003;
    private const uint RIM_TYPEMOUSE = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE { public ushort usUsagePage; public ushort usUsage; public uint dwFlags; public IntPtr hwndTarget; }
    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER { public uint dwType; public uint dwSize; public IntPtr hDevice; public IntPtr wParam; }
    [StructLayout(LayoutKind.Sequential)]
    private struct RAWMOUSE
    {
        public ushort usFlags;
        public uint ulButtons;
        public ushort usButtonFlags;
        public ushort usButtonData;
        public uint ulRawButtons;
        public int lLastX;
        public int lLastY;
        public uint ulExtraInformation;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices([In] RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);
    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);
}

internal static class ControllerDiagnostics
{
    public sealed record DriftResult(string Text, double Score);

    public static async Task<DriftResult> MeasureDriftAsync(TimeSpan duration)
    {
        var controller = FindController();
        if (controller < 0)
            return new DriftResult("لم يتم العثور على يد XInput. يد PlayStation المباشرة تحتاج backend HID خاص؛ D7KT لن يعطي رقمًا وهميًا.", 0);

        double maxL = 0, maxR = 0;
        byte maxLt = 0, maxRt = 0;
        var samples = 0;
        var until = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < until)
        {
            if (XInputGetState((uint)controller, out var state) == 0)
            {
                var g = state.Gamepad;
                maxL = Math.Max(maxL, Radius(g.sThumbLX, g.sThumbLY));
                maxR = Math.Max(maxR, Radius(g.sThumbRX, g.sThumbRY));
                maxLt = Math.Max(maxLt, g.bLeftTrigger);
                maxRt = Math.Max(maxRt, g.bRightTrigger);
                samples++;
            }
            await Task.Delay(4);
        }

        var recommendedL = Math.Min(.40, maxL + .02);
        var recommendedR = Math.Min(.40, maxR + .02);
        var worst = Math.Max(maxL, maxR);
        var score = Math.Clamp(100 - Math.Max(0, worst - .02) * 500, 0, 100);
        var verdict = worst < .03 ? "ممتاز" : worst < .06 ? "جيد" : worst < .10 ? "Drift ملحوظ" : "Drift مرتفع";
        var text = $"Controller #{controller + 1} • Samples {samples:N0} • {verdict}\n" +
                   $"Left drift {maxL * 100:0.00}% → Deadzone ≥ {recommendedL * 100:0.0}% • Right {maxR * 100:0.00}% → ≥ {recommendedR * 100:0.0}%\n" +
                   $"Trigger idle L {maxLt}/255 • R {maxRt}/255 • Drift Score {score:0}/100.";
        return new DriftResult(text, score);
    }

    public static async Task<string> MeasureRangeAsync(TimeSpan duration)
    {
        var controller = FindController();
        if (controller < 0) return "لم يتم العثور على يد XInput.";

        short minLX = short.MaxValue, maxLX = short.MinValue, minLY = short.MaxValue, maxLY = short.MinValue;
        short minRX = short.MaxValue, maxRX = short.MinValue, minRY = short.MaxValue, maxRY = short.MinValue;
        byte maxLT = 0, maxRT = 0;
        double maxLR = 0, maxRR = 0;
        var samples = 0;
        var until = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);

        while (Stopwatch.GetTimestamp() < until)
        {
            if (XInputGetState((uint)controller, out var state) == 0)
            {
                var g = state.Gamepad;
                minLX = Math.Min(minLX, g.sThumbLX); maxLX = Math.Max(maxLX, g.sThumbLX);
                minLY = Math.Min(minLY, g.sThumbLY); maxLY = Math.Max(maxLY, g.sThumbLY);
                minRX = Math.Min(minRX, g.sThumbRX); maxRX = Math.Max(maxRX, g.sThumbRX);
                minRY = Math.Min(minRY, g.sThumbRY); maxRY = Math.Max(maxRY, g.sThumbRY);
                maxLT = Math.Max(maxLT, g.bLeftTrigger); maxRT = Math.Max(maxRT, g.bRightTrigger);
                maxLR = Math.Max(maxLR, Radius(g.sThumbLX, g.sThumbLY));
                maxRR = Math.Max(maxRR, Radius(g.sThumbRX, g.sThumbRY));
                samples++;
            }
            await Task.Delay(4);
        }

        static double Coverage(short min, short max) => Math.Clamp((max - (double)min) / 65535.0 * 100, 0, 100);
        return $"Samples {samples:N0}\n" +
               $"Left X {Coverage(minLX, maxLX):0.0}% • Y {Coverage(minLY, maxLY):0.0}% • max radius {maxLR * 100:0.0}%\n" +
               $"Right X {Coverage(minRX, maxRX):0.0}% • Y {Coverage(minRY, maxRY):0.0}% • max radius {maxRR * 100:0.0}%\n" +
               $"Triggers: LT {maxLT}/255 • RT {maxRT}/255. إذا التغطية منخفضة رغم وصولك للنهاية فراجع calibration/game deadzone.";
    }

    private static int FindController()
    {
        for (var i = 0; i < 4; i++) if (XInputGetState((uint)i, out _) == 0) return i;
        return -1;
    }

    private static double Radius(short x, short y)
    {
        static double N(short v) => (v == short.MinValue ? -32767d : v) / 32767d;
        var nx = N(x); var ny = N(y);
        return Math.Min(1.5, Math.Sqrt(nx * nx + ny * ny));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState(uint dwUserIndex, out XINPUT_STATE pState);
}
