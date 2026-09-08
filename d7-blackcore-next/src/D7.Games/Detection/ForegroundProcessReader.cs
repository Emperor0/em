using System.Diagnostics;
using System.Runtime.InteropServices;

namespace D7.Games.Detection;

public sealed record ForegroundProcessInfo(
    int ProcessId,
    string ProcessName,
    string? ExecutablePath);

public sealed class ForegroundProcessReader
{
    public ForegroundProcessInfo? TryGetForegroundProcess()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        _ = GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return null;

        try
        {
            using var process = Process.GetProcessById(unchecked((int)pid));
            string? path = null;
            try { path = process.MainModule?.FileName; }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }

            return new ForegroundProcessInfo(process.Id, process.ProcessName, path);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
