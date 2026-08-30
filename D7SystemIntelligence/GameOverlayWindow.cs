using D7SystemIntelligence.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace D7SystemIntelligence;

public sealed class GameOverlayWindow : Window, IAsyncDisposable
{
    private readonly string _processName;
    private readonly System.Windows.Controls.TextBlock _text = new();
    private readonly System.Windows.Controls.Border _border;
    private readonly DispatcherTimer _timer;
    private int _pid;
    private int _healthyTicks;

    public GameOverlayWindow(HardwareEngine hardware, string processName)
    {
        // HardwareEngine is intentionally not sampled here. The main D7KT orchestrator already
        // publishes hardware + PresentMon + network data into D7RuntimeBus. HUD is a renderer only.
        _processName = processName;
        Title = "D7KT HUD";
        Width = 430;
        Height = 54;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        Focusable = false;
        Left = 12;
        Top = 12;

        _border = new System.Windows.Controls.Border
        {
            Background = new SolidColorBrush(Color.FromArgb(188, 6, 7, 10)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(110, 225, 29, 46)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(11, 8, 11, 8),
            Child = _text
        };
        _text.Foreground = Brushes.White;
        _text.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        _text.FontSize = 13.5;
        _text.Text = "D7KT HUD • انتظار Game Session…";
        Content = _border;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _timer.Tick += (_, _) => Tick();
        SourceInitialized += (_, _) => MakeClickThrough();
        Loaded += (_, _) => Start();
        Closed += async (_, _) => await DisposeAsync();
    }

    private void Start()
    {
        var process = Process.GetProcessesByName(_processName).OrderByDescending(SafeWorkingSet).FirstOrDefault();
        if (process == null)
        {
            _text.Text = $"D7KT HUD • {_processName} غير شغالة";
            return;
        }
        try { _pid = process.Id; }
        finally { process.Dispose(); }
        _timer.Start();
    }

    private void Tick()
    {
        try
        {
            if (!IsProcessAlive(_pid))
            {
                _text.Text = $"D7KT HUD • {_processName} أغلقت";
                _timer.Stop();
                return;
            }

            var h = D7RuntimeBus.Hardware;
            var s = D7RuntimeBus.SessionSample;
            var context = D7RuntimeBus.Context;
            if (h == null || s == null)
            {
                _text.Text = "D7KT HUD • انتظار Telemetry المشتركة…";
                Height = 54;
                return;
            }

            var problems = new List<string>();
            if (s.P99FrameMs is >= 25) problems.Add($"P99 {s.P99FrameMs:0.0}ms");
            if (h.CpuLoad >= 92 && h.GpuLoad < 96) problems.Add($"CPU {h.CpuLoad:0}%");
            if (h.GpuLoad >= 98) problems.Add($"GPU {h.GpuLoad:0}%");
            if (h.RamLoad >= 90) problems.Add($"RAM {h.RamLoad:0}%");
            if (h.CpuTemp >= 86) problems.Add($"CPU {h.CpuTemp:0}°C");
            if (h.GpuTemp >= 84) problems.Add($"GPU {h.GpuTemp:0}°C");
            if (s.JitterMs is >= 12) problems.Add($"Jitter {s.JitterMs:0.0}ms");
            if (s.PingMs is >= 120) problems.Add($"Ping {s.PingMs:0}ms");

            var fps = F(s.Fps, "0");
            var low = F(s.OnePercentLow, "0");
            var ping = F(s.PingMs, "0");
            var mission = D7MissionEngine.MissionArabic(D7RuntimeBus.Mission);
            var first = $"{fps} FPS  •  1% {low}  •  Ping {ping}ms";

            if (problems.Count == 0)
            {
                _healthyTicks++;
                _text.Text = first;
                Height = 54;
                Width = 365;
                _border.BorderBrush = new SolidColorBrush(Color.FromArgb(65, 70, 212, 141));
            }
            else
            {
                _healthyTicks = 0;
                _text.Text = first +
                             $"\nP99 {F(s.P99FrameMs, "0.0")}ms  •  CPU {h.CpuLoad:0}% {h.CpuTemp:0}°  •  GPU {h.GpuLoad:0}% {h.GpuTemp:0}°  •  RAM {h.RamLoad:0}%" +
                             $"\n⚠ {string.Join(" • ", problems)}  •  {mission}";
                Height = 94;
                Width = 530;
                _border.BorderBrush = new SolidColorBrush(Color.FromArgb(190, 225, 29, 46));
            }

            // Context is used only as a consistency guard. HUD never injects/hook DLLs into the game.
            if (!string.Equals(context?.PrimaryGame, _processName, StringComparison.OrdinalIgnoreCase))
                _text.Text += "\nTelemetry context يتغير — HUD ينتظر تثبيت اللعبة النشطة.";
        }
        catch (Exception ex)
        {
            _text.Text = "D7KT HUD: " + ex.Message;
        }
    }

    private void MakeClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
    }

    private static string F(double? value, string format) => value.HasValue ? value.Value.ToString(format) : "—";
    private static long SafeWorkingSet(Process p) { try { return p.WorkingSet64; } catch { return 0; } }
    private static bool IsProcessAlive(int pid) { try { using var p = Process.GetProcessById(pid); return !p.HasExited; } catch { return false; } }

    public ValueTask DisposeAsync()
    {
        _timer.Stop();
        return ValueTask.CompletedTask;
    }

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x20;
    private const long WS_EX_TOOLWINDOW = 0x80;
    private const long WS_EX_NOACTIVATE = 0x08000000;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) => IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value) => IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, value) : SetWindowLong32(hWnd, nIndex, value);
}
