using System.Diagnostics;

namespace D7.Games.Detection;

public sealed record RunningProcessInfo(
    int ProcessId,
    string ProcessName,
    string? ExecutablePath);

public sealed class RunningProcessReader
{
    public IReadOnlyList<RunningProcessInfo> Snapshot()
    {
        var result = new List<RunningProcessInfo>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id <= 4 || process.HasExited) continue;
                    string? path = null;
                    try { path = process.MainModule?.FileName; }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }

                    result.Add(new RunningProcessInfo(process.Id, process.ProcessName, path));
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    // Process can exit or become protected between enumeration and inspection.
                }
            }
        }
        return result;
    }
}
