using System.Diagnostics;
using System.Globalization;
using D7.Hardware.Native;

namespace D7.Hardware.Telemetry;

public sealed class SystemTelemetrySampler
{
    private readonly object _cpuGate = new();
    private bool _hasCpuBaseline;
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private string? _nvidiaSmiPath;
    private bool _resolvedNvidiaSmi;

    public async Task<SystemTelemetrySample> SampleAsync(CancellationToken cancellationToken)
    {
        var cpu = ReadCpuUtilization();
        var memory = ReadMemory();
        var nvidia = await ReadNvidiaAsync(cancellationToken).ConfigureAwait(false);

        using var process = Process.GetCurrentProcess();
        process.Refresh();

        return new SystemTelemetrySample(
            DateTimeOffset.UtcNow,
            cpu,
            memory.UsedPercent,
            memory.AvailableGiB,
            nvidia,
            Math.Round(process.TotalProcessorTime.TotalSeconds, 3),
            process.WorkingSet64,
            process.PrivateMemorySize64,
            process.Threads.Count);
    }

    public double? ReadCpuUtilization()
    {
        if (!NativeMethods.GetSystemTimes(out var idleTime, out var kernelTime, out var userTime)) return null;

        var idle = idleTime.ToUInt64();
        var kernel = kernelTime.ToUInt64();
        var user = userTime.ToUInt64();

        lock (_cpuGate)
        {
            if (!_hasCpuBaseline)
            {
                _previousIdle = idle;
                _previousKernel = kernel;
                _previousUser = user;
                _hasCpuBaseline = true;
                return null;
            }

            var idleDelta = idle - _previousIdle;
            var kernelDelta = kernel - _previousKernel;
            var userDelta = user - _previousUser;
            _previousIdle = idle;
            _previousKernel = kernel;
            _previousUser = user;

            var total = kernelDelta + userDelta;
            if (total == 0) return null;
            var busy = total > idleDelta ? total - idleDelta : 0;
            return Math.Round(Math.Clamp(busy * 100d / total, 0d, 100d), 1);
        }
    }

    private static (double UsedPercent, double AvailableGiB) ReadMemory()
    {
        var status = new NativeMethods.MemoryStatusEx();
        if (!NativeMethods.GlobalMemoryStatusEx(status) || status.ullTotalPhys == 0) return (0, 0);
        var usedPercent = (1d - status.ullAvailPhys / (double)status.ullTotalPhys) * 100d;
        return (Math.Round(usedPercent, 1), Math.Round(status.ullAvailPhys / 1073741824d, 2));
    }

    public async Task<NvidiaTelemetry> ReadNvidiaAsync(CancellationToken cancellationToken)
    {
        var path = ResolveNvidiaSmi();
        if (path is null)
        {
            return new NvidiaTelemetry(false, null, null, null, null, null, null, null, null, "nvidia-smi غير متاح.");
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = path,
                Arguments = "--query-gpu=name,utilization.gpu,temperature.gpu,power.draw,clocks.current.graphics,memory.used,memory.total,driver_version --format=csv,noheader,nounits",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        try
        {
            if (!process.Start())
                return new NvidiaTelemetry(false, null, null, null, null, null, null, null, null, "تعذر تشغيل nvidia-smi.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                return new NvidiaTelemetry(false, null, null, null, null, null, null, null, null, "انتهت مهلة قراءة NVIDIA.");
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var line = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(line))
                return new NvidiaTelemetry(false, null, null, null, null, null, null, null, null, string.IsNullOrWhiteSpace(error) ? "لم تعد NVIDIA بيانات." : error.Trim());

            var columns = line.Split(',').Select(x => x.Trim()).ToArray();
            if (columns.Length < 8)
                return new NvidiaTelemetry(false, null, null, null, null, null, null, null, null, "صيغة بيانات NVIDIA غير متوقعة.");

            return new NvidiaTelemetry(
                true,
                columns[0],
                ParseDouble(columns[1]),
                ParseDouble(columns[2]),
                ParseDouble(columns[3]),
                ParseDouble(columns[4]),
                ParseDouble(columns[5]),
                ParseDouble(columns[6]),
                columns[7],
                null);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        catch (Exception ex)
        {
            TryKill(process);
            return new NvidiaTelemetry(false, null, null, null, null, null, null, null, null, ex.Message);
        }
    }

    private string? ResolveNvidiaSmi()
    {
        if (_resolvedNvidiaSmi) return _nvidiaSmiPath;
        _resolvedNvidiaSmi = true;

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var candidates = new List<string>
        {
            Path.Combine(windows, "System32", "nvidia-smi.exe"),
            Path.Combine(programFiles, "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe")
        };

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        candidates.AddRange(pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory.Trim(), "nvidia-smi.exe")));

        _nvidiaSmiPath = candidates.FirstOrDefault(File.Exists);
        return _nvidiaSmiPath;
    }

    private static double? ParseDouble(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
