using D7SystemIntelligence.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace D7SystemIntelligence;

public sealed class InputLabWindow : Window
{
    private readonly PhysicalPeripheralIntelligence _physical = new();
    private readonly DataGrid _devices = new();
    private readonly TextBlock _mouseResult = new();
    private readonly TextBlock _controllerResult = new();
    private readonly List<long> _mouseTicks = [];
    private HwndSource? _source;
    private bool _measuringMouse;
    private long _mouseStart;

    public InputLabWindow()
    {
        Title = "D7 — مختبر الإدخال والأجهزة الطرفية";
        Width = 1120;
        Height = 760;
        MinWidth = 920;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("Bg", Brushes.Black);
        Foreground = Brush("Text", Brushes.White);

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "مختبر الإدخال الحقيقي", FontSize = 28, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock
        {
            Text = "يجمع واجهات HID التابعة لنفس الجهاز، ويقيس Polling الفعلي للماوس من WM_INPUT، وDrift ليد XInput. لا يختلق أرقام latency غير قابلة للقياس برمجيًا.",
            Foreground = Brush("Muted", Brushes.Gray),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 12)
        });
        var scan = new Button { Content = "فحص الأجهزة الحقيقية", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 170 };
        scan.Click += async (_, _) => await RefreshDevicesAsync();
        header.Children.Add(scan);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        _devices.AutoGenerateColumns = false;
        _devices.IsReadOnly = true;
        _devices.Margin = new Thickness(0, 14, 0, 14);
        _devices.Columns.Add(new DataGridTextColumn { Header = "الفئة", Binding = new System.Windows.Data.Binding(nameof(PhysicalPeripheralRecord.Category)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _devices.Columns.Add(new DataGridTextColumn { Header = "الجهاز", Binding = new System.Windows.Data.Binding(nameof(PhysicalPeripheralRecord.Name)), Width = new DataGridLength(2.4, DataGridLengthUnitType.Star) });
        _devices.Columns.Add(new DataGridTextColumn { Header = "الاتصال", Binding = new System.Windows.Data.Binding(nameof(PhysicalPeripheralRecord.Transport)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _devices.Columns.Add(new DataGridTextColumn { Header = "واجهات", Binding = new System.Windows.Data.Binding(nameof(PhysicalPeripheralRecord.InterfaceCount)), Width = new DataGridLength(.7, DataGridLengthUnitType.Star) });
        _devices.Columns.Add(new DataGridTextColumn { Header = "الحالة", Binding = new System.Windows.Data.Binding(nameof(PhysicalPeripheralRecord.Status)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        Grid.SetRow(_devices, 1);
        root.Children.Add(_devices);

        var labs = new UniformGrid { Columns = 2 };
        labs.Children.Add(BuildMousePanel());
        labs.Children.Add(BuildControllerPanel());
        Grid.SetRow(labs, 2);
        root.Children.Add(labs);

        Content = root;
        SourceInitialized += OnSourceInitialized;
        Loaded += async (_, _) => await RefreshDevicesAsync();
        Closed += (_, _) => _source?.RemoveHook(WndProc);
    }

    private Border BuildMousePanel()
    {
        var panel = Card();
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Mouse Polling", FontSize = 20, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock
        {
            Text = "حرّك الماوس بسرعة وباستمرار لمدة 5 ثوانٍ. D7 يحسب معدل وصول Raw Input الحقيقي والجِتر.",
            Foreground = Brush("Muted", Brushes.Gray), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 8)
        });
        _mouseResult.Text = "لم يبدأ الاختبار بعد.";
        _mouseResult.TextWrapping = TextWrapping.Wrap;
        _mouseResult.Margin = new Thickness(0, 6, 0, 8);
        stack.Children.Add(_mouseResult);
        var button = new Button { Content = "ابدأ اختبار 5 ثوانٍ", HorizontalAlignment = HorizontalAlignment.Right };
        button.Click += (_, _) => StartMousePollingTest(button);
        stack.Children.Add(button);
        panel.Child = stack;
        return panel;
    }

    private Border BuildControllerPanel()
    {
        var panel = Card();
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Controller Drift", FontSize = 20, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock
        {
            Text = "اترك الأنالوج بدون لمس لمدة 5 ثوانٍ. D7 يقيس أقصى انحراف خام عبر XInput ويقترح Deadzone فوق الانحراف بقليل.",
            Foreground = Brush("Muted", Brushes.Gray), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 8)
        });
        _controllerResult.Text = "لم يبدأ الاختبار بعد.";
        _controllerResult.TextWrapping = TextWrapping.Wrap;
        _controllerResult.Margin = new Thickness(0, 6, 0, 8);
        stack.Children.Add(_controllerResult);
        var button = new Button { Content = "ابدأ Drift Test", HorizontalAlignment = HorizontalAlignment.Right };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            _controllerResult.Text = "جاري أخذ عينات لمدة 5 ثوانٍ… لا تلمس الأنالوج.";
            try { _controllerResult.Text = await ControllerDriftDiagnostics.MeasureAsync(TimeSpan.FromSeconds(5)); }
            finally { button.IsEnabled = true; }
        };
        stack.Children.Add(button);
        panel.Child = stack;
        return panel;
    }

    private async Task RefreshDevicesAsync()
    {
        _devices.ItemsSource = await _physical.ScanAsync();
    }

    private void StartMousePollingTest(Button button)
    {
        if (_source == null)
        {
            _mouseResult.Text = "تعذر إنشاء Raw Input hook لهذه النافذة.";
            return;
        }

        _mouseTicks.Clear();
        _mouseStart = Stopwatch.GetTimestamp();
        _measuringMouse = true;
        button.IsEnabled = false;
        _mouseResult.Text = "الآن حرّك الماوس بسرعة وباستمرار…";

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _measuringMouse = false;
            button.IsEnabled = true;
            _mouseResult.Text = BuildMouseResult();
        };
        timer.Start();
    }

    private string BuildMouseResult()
    {
        if (_mouseTicks.Count < 30)
            return $"وصلت {_mouseTicks.Count} حزمة فقط. حرّك الماوس أكثر أثناء الاختبار ثم أعده.";

        var intervals = new List<double>(_mouseTicks.Count - 1);
        for (var i = 1; i < _mouseTicks.Count; i++)
            intervals.Add((_mouseTicks[i] - _mouseTicks[i - 1]) * 1000.0 / Stopwatch.Frequency);

        intervals = intervals.Where(x => x > 0.05 && x < 100).OrderBy(x => x).ToList();
        if (intervals.Count < 20) return "العينات غير كافية لحساب Polling موثوق.";

        var median = Percentile(intervals, .50);
        var p95 = Percentile(intervals, .95);
        var avg = intervals.Average();
        var variance = intervals.Sum(x => Math.Pow(x - avg, 2)) / intervals.Count;
        var jitter = Math.Sqrt(variance);
        var hz = median <= 0 ? 0 : 1000.0 / median;
        var rounded = SnapPollingRate(hz);

        return $"الحزم: {_mouseTicks.Count:N0} | Polling مقاس ≈ {hz:0} Hz (أقرب قيمة {rounded} Hz) | Median {median:0.###} ms | P95 {p95:0.###} ms | Jitter σ {jitter:0.###} ms.";
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
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(15),
        Margin = new Thickness(5)
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

internal static class ControllerDriftDiagnostics
{
    public static async Task<string> MeasureAsync(TimeSpan duration)
    {
        var controller = Enumerable.Range(0, 4).FirstOrDefault(i => XInputGetState((uint)i, out _) == 0);
        if (XInputGetState((uint)controller, out _) != 0)
            return "لم يتم العثور على يد XInput متصلة. يد PlayStation عبر DirectInput تحتاج backend مختلف وسيتم عرضها كغير مدعومة بدل إعطاء رقم وهمي.";

        double maxL = 0, maxR = 0;
        byte maxLt = 0, maxRt = 0;
        var samples = 0;
        var until = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);

        while (Stopwatch.GetTimestamp() < until)
        {
            if (XInputGetState((uint)controller, out var state) == 0)
            {
                var g = state.Gamepad;
                maxL = Math.Max(maxL, Math.Sqrt(Norm(g.sThumbLX) * Norm(g.sThumbLX) + Norm(g.sThumbLY) * Norm(g.sThumbLY)));
                maxR = Math.Max(maxR, Math.Sqrt(Norm(g.sThumbRX) * Norm(g.sThumbRX) + Norm(g.sThumbRY) * Norm(g.sThumbRY)));
                maxLt = Math.Max(maxLt, g.bLeftTrigger);
                maxRt = Math.Max(maxRt, g.bRightTrigger);
                samples++;
            }
            await Task.Delay(4);
        }

        var recommendedL = Math.Min(.40, maxL + .02);
        var recommendedR = Math.Min(.40, maxR + .02);
        return $"Controller #{controller + 1} | عينات {samples:N0} | Left drift {maxL * 100:0.00}% → Deadzone مقترح ≥ {recommendedL * 100:0.0}% | Right drift {maxR * 100:0.00}% → ≥ {recommendedR * 100:0.0}% | Trigger idle L {maxLt}/255, R {maxRt}/255.";
    }

    private static double Norm(short v) => Math.Abs(v == short.MinValue ? short.MaxValue : v) / 32767.0;

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
    private struct XINPUT_STATE { public uint dwPacketNumber; public XINPUT_GAMEPAD Gamepad; }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState(uint dwUserIndex, out XINPUT_STATE pState);
}
