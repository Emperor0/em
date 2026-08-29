using System.Text.Json;

namespace D7SystemIntelligence.Core;

public sealed record BenchmarkSnapshot(
    string Id,
    string Label,
    string Game,
    DateTimeOffset StartedAt,
    double DurationSeconds,
    int Samples,
    double? AverageFps,
    double? AverageOnePercentLow,
    double? AverageP99FrameMs,
    double? WorstP99FrameMs,
    double AverageCpuLoad,
    double AverageGpuLoad,
    double MaxCpuTemp,
    double MaxGpuTemp,
    double AverageRamLoad,
    double? AveragePingMs,
    string FilePath);

public sealed record BenchmarkComparison(
    BenchmarkSnapshot Baseline,
    BenchmarkSnapshot Candidate,
    double? FpsDeltaPercent,
    double? OnePercentLowDeltaPercent,
    double? P99DeltaPercent,
    double CpuLoadDelta,
    double GpuLoadDelta,
    double CpuTempDelta,
    double GpuTempDelta,
    string Verdict);

public sealed class BenchmarkLabService
{
    private readonly GameSessionService _sessions;
    private readonly string _root;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public BenchmarkLabService(GameSessionService sessions)
    {
        _sessions = sessions;
        _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D7SystemIntelligence", "Benchmarks");
        Directory.CreateDirectory(_root);
    }

    public async Task<BenchmarkSnapshot> CaptureAsync(string label, TimeSpan duration, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!_sessions.IsRunning || string.IsNullOrWhiteSpace(_sessions.ActiveGame))
            throw new InvalidOperationException("ابدأ لعبة أولًا. Benchmark Lab يعيد استخدام Telemetry الجلسة نفسها حتى لا يشغل PresentMon ثاني.");

        duration = duration < TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : duration > TimeSpan.FromMinutes(10) ? TimeSpan.FromMinutes(10) : duration;
        label = SanitizeLabel(label);
        var game = _sessions.ActiveGame!;
        var samples = new List<GameSessionSample>();
        var gate = new object();
        void OnSample(GameSessionSample sample) { lock (gate) samples.Add(sample); }

        var start = DateTimeOffset.Now;
        _sessions.SampleUpdated += OnSample;
        try
        {
            while (DateTimeOffset.Now - start < duration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = duration - (DateTimeOffset.Now - start);
                progress?.Report($"Benchmark {label} • {Math.Max(0, remaining.TotalSeconds):0} ث متبقية • Samples {samples.Count}");
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
        finally { _sessions.SampleUpdated -= OnSample; }

        GameSessionSample[] data;
        lock (gate) data = samples.ToArray();
        if (data.Length < 5) throw new InvalidOperationException("لم تصل عينات كافية من Game Session. تأكد أن اللعبة ما زالت شغالة وأن PresentMon يعمل.");

        var fps = data.Where(x => x.Fps is > 0).Select(x => x.Fps!.Value).ToArray();
        var lows = data.Where(x => x.OnePercentLow is > 0).Select(x => x.OnePercentLow!.Value).ToArray();
        var p99 = data.Where(x => x.P99FrameMs is > 0).Select(x => x.P99FrameMs!.Value).ToArray();
        var ping = data.Where(x => x.PingMs is > 0).Select(x => x.PingMs!.Value).ToArray();
        var id = $"{start:yyyyMMdd-HHmmss}-{SanitizeFile(game)}-{SanitizeFile(label)}";
        var path = Path.Combine(_root, id + ".json");
        var snapshot = new BenchmarkSnapshot(
            id, label, game, start, (DateTimeOffset.Now - start).TotalSeconds, data.Length,
            fps.Length > 0 ? fps.Average() : null,
            lows.Length > 0 ? lows.Average() : null,
            p99.Length > 0 ? p99.Average() : null,
            p99.Length > 0 ? p99.Max() : null,
            data.Average(x => x.CpuLoad), data.Average(x => x.GpuLoad), data.Max(x => x.CpuTemp), data.Max(x => x.GpuTemp), data.Average(x => x.RamLoad),
            ping.Length > 0 ? ping.Average() : null, path);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(snapshot, Options), cancellationToken);
        return snapshot;
    }

    public IReadOnlyList<BenchmarkSnapshot> List(int max = 100)
    {
        if (!Directory.Exists(_root)) return [];
        var list = new List<BenchmarkSnapshot>();
        foreach (var file in Directory.EnumerateFiles(_root, "*.json").OrderByDescending(x => x).Take(Math.Max(1, max)))
        {
            try
            {
                var b = JsonSerializer.Deserialize<BenchmarkSnapshot>(File.ReadAllText(file), Options);
                if (b != null) list.Add(b with { FilePath = file });
            }
            catch { }
        }
        return list.OrderByDescending(x => x.StartedAt).ToArray();
    }

    public BenchmarkComparison Compare(BenchmarkSnapshot baseline, BenchmarkSnapshot candidate)
    {
        var fps = PercentDelta(baseline.AverageFps, candidate.AverageFps);
        var low = PercentDelta(baseline.AverageOnePercentLow, candidate.AverageOnePercentLow);
        // Lower P99 is better, so invert the sign for an intuitive "positive = improvement".
        var p99Raw = PercentDelta(baseline.AverageP99FrameMs, candidate.AverageP99FrameMs);
        var p99 = p99Raw.HasValue ? -p99Raw.Value : null;
        var cpu = candidate.AverageCpuLoad - baseline.AverageCpuLoad;
        var gpu = candidate.AverageGpuLoad - baseline.AverageGpuLoad;
        var cpuTemp = candidate.MaxCpuTemp - baseline.MaxCpuTemp;
        var gpuTemp = candidate.MaxGpuTemp - baseline.MaxGpuTemp;

        var scoreParts = new[] { fps, low, p99 }.Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        var score = scoreParts.Length > 0 ? scoreParts.Average() : 0;
        var verdict = score switch
        {
            >= 5 => "تحسن واضح — Candidate أفضل في القياسات الأساسية.",
            >= 1.5 => "تحسن صغير لكنه قابل للقياس.",
            <= -5 => "تراجع واضح — ارجع التغيير إذا كانت ظروف الاختبار متقاربة.",
            <= -1.5 => "تراجع صغير قابل للقياس.",
            _ => "الفرق داخل نطاق صغير؛ لا يوجد دليل قوي أن التغيير حسّن الأداء."
        };
        if (!baseline.Game.Equals(candidate.Game, StringComparison.OrdinalIgnoreCase))
            verdict = "تنبيه: المقارنتان من لعبتين مختلفتين؛ الأرقام ليست A/B صالحة. " + verdict;

        return new BenchmarkComparison(baseline, candidate, fps, low, p99, cpu, gpu, cpuTemp, gpuTemp, verdict);
    }

    private static double? PercentDelta(double? baseline, double? candidate)
    {
        if (!baseline.HasValue || !candidate.HasValue || Math.Abs(baseline.Value) < .0001) return null;
        return (candidate.Value - baseline.Value) * 100d / baseline.Value;
    }

    private static string SanitizeLabel(string value)
        => string.IsNullOrWhiteSpace(value) ? "Benchmark" : value.Trim().Length > 80 ? value.Trim()[..80] : value.Trim();
    private static string SanitizeFile(string text)
        => string.Concat((text ?? string.Empty).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
