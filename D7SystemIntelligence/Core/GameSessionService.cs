using System.Diagnostics;
using System.Text.Json;

namespace D7SystemIntelligence.Core;

public sealed record GameSessionSample(
    DateTimeOffset At,
    double? Fps,
    double? OnePercentLow,
    double? P95FrameMs,
    double? P99FrameMs,
    double CpuLoad,
    double GpuLoad,
    double CpuTemp,
    double GpuTemp,
    double RamLoad,
    double? VramLoad,
    double? PingMs,
    double? JitterMs);

public sealed record StutterEvent(
    DateTimeOffset At,
    double WorstFrameMs,
    double P99FrameMs,
    double CpuLoad,
    double GpuLoad,
    double RamLoad,
    double CpuTemp,
    double GpuTemp,
    string LikelyCause);

public sealed record GameSessionReport(
    string Id,
    string Game,
    int ProcessId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    double DurationMinutes,
    double? AverageFps,
    double? AverageOnePercentLow,
    double? WorstP99FrameMs,
    double MaxCpuLoad,
    double MaxGpuLoad,
    double MaxCpuTemp,
    double MaxGpuTemp,
    double MaxRamLoad,
    double? AveragePingMs,
    int StutterCount,
    IReadOnlyList<StutterEvent> Stutters,
    string Summary,
    string FilePath);

public sealed class GameSessionService : IAsyncDisposable
{
    private readonly HardwareEngine _hardware;
    private readonly NetworkIntelligence _network = new();
    private readonly ManagedPresentMonService _presentMon = new();
    private readonly string _root;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private FrameMetricsMonitor? _frames;
    private readonly List<GameSessionSample> _samples = new();
    private readonly List<StutterEvent> _stutters = new();
    private readonly object _gate = new();
    private DateTimeOffset _started;
    private string _game = string.Empty;
    private int _pid;
    private double? _ping;
    private double? _jitter;
    private DateTimeOffset _lastNetwork;

    public event Action<string>? StatusChanged;
    public bool IsRunning => _cts is { IsCancellationRequested: false };
    public string? ActiveGame => IsRunning ? _game : null;

    public GameSessionService(HardwareEngine hardware)
    {
        _hardware = hardware;
        _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D7SystemIntelligence", "Sessions");
        Directory.CreateDirectory(_root);
    }

    public async Task<string> StartAsync(string processName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(processName)) return "اسم اللعبة غير صالح.";
        if (IsRunning && string.Equals(_game, processName, StringComparison.OrdinalIgnoreCase)) return $"جلسة {_game} شغالة بالفعل.";
        if (IsRunning) await StopAsync(cancellationToken);

        var process = Process.GetProcessesByName(processName).OrderByDescending(SafeWorkingSet).FirstOrDefault();
        if (process == null) return $"لم يجد D7 عملية {processName} شغالة.";
        try { _pid = process.Id; }
        finally { process.Dispose(); }

        _game = processName;
        _started = DateTimeOffset.Now;
        _lastNetwork = DateTimeOffset.MinValue;
        lock (_gate) { _samples.Clear(); _stutters.Clear(); }

        _frames = _presentMon.CreateMonitor();
        var progress = new Progress<double>(p => StatusChanged?.Invoke($"تجهيز PresentMon للجلسة… {p:0}%"));
        await _presentMon.EnsureAsync(progress, cancellationToken);
        await _frames.StartAsync(_pid, cancellationToken);

        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
        StatusChanged?.Invoke($"بدأ Stutter Black Box • {_game} • PID {_pid}");
        return $"بدأ تسجيل جلسة {_game} فعليًا.";
    }

    public async Task<GameSessionReport?> StopAsync(CancellationToken cancellationToken = default)
    {
        var cts = _cts;
        if (cts == null) return null;
        _cts = null;
        try { cts.Cancel(); } catch { }
        if (_loop != null)
        {
            try { await _loop.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken); } catch { }
            _loop = null;
        }
        cts.Dispose();
        if (_frames != null)
        {
            await _frames.DisposeAsync();
            _frames = null;
        }

        var report = BuildReport();
        if (report != null)
        {
            var path = await SaveAsync(report, cancellationToken);
            report = report with { FilePath = path };
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, JsonOptions), cancellationToken);
            StatusChanged?.Invoke(report.Summary);
        }
        _game = string.Empty;
        _pid = 0;
        return report;
    }

    public IReadOnlyList<GameSessionReport> ListRecent(int max = 50)
    {
        if (!Directory.Exists(_root)) return [];
        var list = new List<GameSessionReport>();
        foreach (var file in Directory.EnumerateFiles(_root, "*.json").OrderByDescending(x => x).Take(Math.Max(1, max)))
        {
            try
            {
                var report = JsonSerializer.Deserialize<GameSessionReport>(File.ReadAllText(file), JsonOptions);
                if (report != null) list.Add(report with { FilePath = file });
            }
            catch { }
        }
        return list.OrderByDescending(x => x.StartedAt).ToArray();
    }

    private async Task LoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && IsProcessAlive(_pid))
            {
                var hardware = _hardware.Read();
                var frame = _frames?.Read();
                var recent = _frames?.DrainRecentFrameTimes() ?? [];

                if ((DateTimeOffset.Now - _lastNetwork).TotalSeconds >= 10)
                {
                    try
                    {
                        var n = await _network.ScanAsync(token);
                        _ping = n.InternetLatencyMs;
                        _jitter = n.JitterMs;
                    }
                    catch { }
                    _lastNetwork = DateTimeOffset.Now;
                }

                var sample = new GameSessionSample(
                    DateTimeOffset.Now,
                    frame?.Fps,
                    frame?.OnePercentLow,
                    frame?.P95FrameMs,
                    frame?.P99FrameMs,
                    hardware.CpuLoad,
                    hardware.GpuLoad,
                    hardware.CpuTemp,
                    hardware.GpuTemp,
                    hardware.RamLoad,
                    hardware.VramLoad,
                    _ping,
                    _jitter);

                lock (_gate)
                {
                    _samples.Add(sample);
                    if (_samples.Count > 14400) _samples.RemoveRange(0, _samples.Count - 14400); // ~4h @1Hz
                }

                if (recent.Length > 0)
                {
                    var worst = recent.Max();
                    var p99 = frame?.P99FrameMs ?? Percentile(recent, .99);
                    if (worst >= 50 || p99 >= 33.3)
                    {
                        var cause = ClassifyStutter(hardware, p99, _ping, _jitter);
                        var ev = new StutterEvent(DateTimeOffset.Now, worst, p99, hardware.CpuLoad, hardware.GpuLoad, hardware.RamLoad, hardware.CpuTemp, hardware.GpuTemp, cause);
                        lock (_gate)
                        {
                            if (_stutters.Count == 0 || (ev.At - _stutters[^1].At).TotalSeconds >= 2)
                                _stutters.Add(ev);
                        }
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { StatusChanged?.Invoke("Stutter Black Box: " + ex.Message); }
    }

    private GameSessionReport? BuildReport()
    {
        GameSessionSample[] samples;
        StutterEvent[] stutters;
        lock (_gate) { samples = _samples.ToArray(); stutters = _stutters.ToArray(); }
        if (samples.Length == 0) return null;

        var ended = DateTimeOffset.Now;
        var fps = samples.Where(x => x.Fps.HasValue && x.Fps > 0).Select(x => x.Fps!.Value).ToArray();
        var lows = samples.Where(x => x.OnePercentLow.HasValue && x.OnePercentLow > 0).Select(x => x.OnePercentLow!.Value).ToArray();
        var p99s = samples.Where(x => x.P99FrameMs.HasValue).Select(x => x.P99FrameMs!.Value).ToArray();
        var pings = samples.Where(x => x.PingMs.HasValue).Select(x => x.PingMs!.Value).ToArray();
        var id = $"{_started:yyyyMMdd-HHmmss}-{Sanitize(_game)}";
        var summary = $"جلسة {_game} • {(ended - _started).TotalMinutes:0.0} دقيقة • Avg FPS {(fps.Length > 0 ? fps.Average().ToString("0") : "—")} • 1% {(lows.Length > 0 ? lows.Average().ToString("0") : "—")} • Stutters {stutters.Length}";

        return new GameSessionReport(
            id, _game, _pid, _started, ended, (ended - _started).TotalMinutes,
            fps.Length > 0 ? fps.Average() : null,
            lows.Length > 0 ? lows.Average() : null,
            p99s.Length > 0 ? p99s.Max() : null,
            samples.Max(x => x.CpuLoad), samples.Max(x => x.GpuLoad), samples.Max(x => x.CpuTemp), samples.Max(x => x.GpuTemp), samples.Max(x => x.RamLoad),
            pings.Length > 0 ? pings.Average() : null,
            stutters.Length, stutters, summary, string.Empty);
    }

    private async Task<string> SaveAsync(GameSessionReport report, CancellationToken token)
    {
        var path = Path.Combine(_root, report.Id + ".json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, JsonOptions), token);
        return path;
    }

    private static string ClassifyStutter(HardwareSnapshot h, double p99, double? ping, double? jitter)
    {
        if (h.CpuLoad >= 92 && h.GpuLoad < 92) return "CPU pressure / CPU-side bottleneck محتمل";
        if (h.GpuLoad >= 98) return "GPU saturation محتمل";
        if (h.RamLoad >= 90) return "RAM pressure محتمل";
        if (h.CpuTemp >= 88 || h.GpuTemp >= 86) return "Thermal pressure محتمل";
        if (jitter >= 12 || ping >= 120) return "Network instability متزامن؛ لا يثبت أنه سبب frametime";
        if (p99 >= 50) return "Frame-time spike قوي؛ يحتاج مقارنة بالعمليات/التخزين في نفس الوقت";
        return "Stutter مرصود بدون سبب واحد واضح من القياسات الحالية";
    }

    private static double Percentile(double[] values, double p)
    {
        if (values.Length == 0) return 0;
        var sorted = values.OrderBy(x => x).ToArray();
        var index = Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1);
        return sorted[index];
    }

    private static long SafeWorkingSet(Process p) { try { return p.WorkingSet64; } catch { return 0; } }
    private static bool IsProcessAlive(int pid) { try { using var p = Process.GetProcessById(pid); return !p.HasExited; } catch { return false; } }
    private static string Sanitize(string text) => string.Concat(text.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public async ValueTask DisposeAsync()
    {
        if (IsRunning) await StopAsync();
        if (_frames != null) await _frames.DisposeAsync();
    }
}
