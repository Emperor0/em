using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace D7SystemIntelligence;

internal static class ModernWindowChromeBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is Window window) ModernWindowChrome.Apply(window);
            }),
            true);
    }
}

internal static class ModernWindowChrome
{
    public static void Apply(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            var enabled = 1;
            // 20 is current immersive dark title bar; 19 is used by older Win10 builds.
            if (DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int)) != 0)
                _ = DwmSetWindowAttribute(hwnd, 19, ref enabled, sizeof(int));

            // Rounded corners on supported Windows 11 builds. Ignored safely on Win10.
            var round = 2;
            _ = DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
        }
        catch
        {
            // Appearance enhancement only; never block a D7 feature if DWM does not support an attribute.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);
}
