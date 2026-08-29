using D7SystemIntelligence.Core;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace D7SystemIntelligence;

public partial class MainWindow
{
    private const int ShadowCaptureHotkeyId = 0xD701;
    private const int WmHotkey = 0x0312;

    private readonly ShadowCaptureService _shadowCapture = new();
    private bool _shadowCaptureUiInjected;
    private HwndSource? _shadowCaptureHwndSource;
    private IntPtr _shadowCaptureWindowHandle;

    static MainWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => ((MainWindow)sender).InitializeShadowCaptureFeature()),
            true);
    }

    private void InitializeShadowCaptureFeature()
    {
        if (_shadowCaptureUiInjected) return;
        _shadowCaptureUiInjected = true;

        InjectShadowCaptureButton();
        RegisterShadowCaptureHotkey();

        Closed += (_, _) =>
        {
            UnregisterShadowCaptureHotkey();
            _ = _shadowCapture.DisposeAsync().AsTask();
        };
    }

    private void InjectShadowCaptureButton()
    {
        var sidebar = FindVisualChildren<StackPanel>(this)
            .FirstOrDefault(stack => stack.Children.OfType<Button>()
                .Any(button => string.Equals(button.Content?.ToString(), "التحديثات والإصلاح", StringComparison.Ordinal)));

        if (sidebar == null) return;
        if (sidebar.Children.OfType<Button>().Any(button => string.Equals(button.Content?.ToString(), "تصوير المقاطع", StringComparison.Ordinal)))
            return;

        var updateButton = sidebar.Children.OfType<Button>()
            .First(button => string.Equals(button.Content?.ToString(), "التحديثات والإصلاح", StringComparison.Ordinal));
        var index = sidebar.Children.IndexOf(updateButton);

        var captureButton = new Button { Content = "تصوير المقاطع" };
        captureButton.Click += (_, _) =>
        {
            var window = new ShadowCaptureWindow(_shadowCapture) { Owner = this };
            window.Closed += (_, _) => RegisterShadowCaptureHotkey();
            window.ShowDialog();
        };

        sidebar.Children.Insert(index, captureButton);
    }

    private void RegisterShadowCaptureHotkey()
    {
        UnregisterShadowCaptureHotkey();

        _shadowCaptureWindowHandle = new WindowInteropHelper(this).Handle;
        if (_shadowCaptureWindowHandle == IntPtr.Zero) return;

        _shadowCaptureHwndSource = HwndSource.FromHwnd(_shadowCaptureWindowHandle);
        _shadowCaptureHwndSource?.AddHook(ShadowCaptureWndProc);

        var settings = _shadowCapture.LoadSettings();
        var virtualKey = ParseFunctionKey(settings.SaveHotkey);
        if (virtualKey == 0) virtualKey = 0x77; // F8

        if (!RegisterHotKey(_shadowCaptureWindowHandle, ShadowCaptureHotkeyId, 0, (uint)virtualKey))
            StatusText.Text = $"D7 يعمل، لكن تعذر حجز Hotkey {settings.SaveHotkey} لتصوير المقاطع. اختر زرًا آخر من صفحة تصوير المقاطع.";
    }

    private void UnregisterShadowCaptureHotkey()
    {
        if (_shadowCaptureWindowHandle != IntPtr.Zero)
            UnregisterHotKey(_shadowCaptureWindowHandle, ShadowCaptureHotkeyId);

        if (_shadowCaptureHwndSource != null)
            _shadowCaptureHwndSource.RemoveHook(ShadowCaptureWndProc);

        _shadowCaptureHwndSource = null;
        _shadowCaptureWindowHandle = IntPtr.Zero;
    }

    private IntPtr ShadowCaptureWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == ShadowCaptureHotkeyId)
        {
            handled = true;
            _ = SaveShadowReplayFromHotkeyAsync();
        }
        return IntPtr.Zero;
    }

    private async Task SaveShadowReplayFromHotkeyAsync()
    {
        try
        {
            StatusText.Text = "D7: جاري حفظ آخر مقطع…";
            var result = await _shadowCapture.SaveReplayAsync();
            StatusText.Text = result.Replace(Environment.NewLine, " • ");
        }
        catch (Exception ex)
        {
            StatusText.Text = "D7 Shadow Capture: " + ex.Message;
        }
    }

    private static int ParseFunctionKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var text = value.Trim().ToUpperInvariant();
        if (!text.StartsWith('F') || !int.TryParse(text[1..], out var number) || number is < 1 or > 24)
            return 0;
        return 0x70 + number - 1;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root == null) yield break;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
