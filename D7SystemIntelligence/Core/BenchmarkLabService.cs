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
    string FilePath,
    long FrameCount = 0,
    double? PointOnePercentLow = null,
    double? P95FrameMs = null,
    double? P999FrameMs = null,
    double? MaxFrameMs = null,
    double? FrameTimeCvPercent = null,
    string Quality = "Legacy/Aggregated");

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
    string Verdict,
    double? PointOneLowDeltaPercent = null,
    double? WeightedScore = null,
    string Confidence = "Unknown");

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
            throw new InvalidOperationException("ابدأ لعبة أولًا. Benchmark Lab يعيد استخدام PresentMon الخاص بجلسة D7KT بدل تشغيل Monitor ثاني.");

        duration = duration < TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : duration > TimeSpan.FromMinutes(10) ? TimeSpan.FromMinutes(10) : duration;
        label = SanitizeLabel(label);
        var game = _sessions.ActiveGame!;
        var samples = new List<GameSessionSample>();
        var frameTimes = new List<double>();
        var gate = new object();

        void OnSample(GameSessionSample sample)
        {
            lock (gate) samples.Add(sample);
        }

        void OnFrames(IReadOnlyList<double> frames)
        {
            if (frames.Count == 0) return;
            lock (gate)
            {
                foreach (var ms in frames)
                    if (ms is > 0.05 and < 1000) frameTimes.Add(ms);
            }
        }

        var start = DateTimeOffset.Now;
        _sessions.SampleUpdated += OnSample;
        _sessions.FrameTimesUpdated += OnFrames;
        try
        {
            while (DateTimeOffset.Now - start < duration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = duration - (DateTimeOffset.Now - start);
                int sampleCount;
                int frameCount;
                lock (gate) { sampleCount = samples.Count; frameCount = frameTimes.Count; }
                progress?.Report($"Benchmark {label} • {Math.Max(0, remaining.TotalSeconds):0} ث • telemetry {sampleCount} • raw frames {frameCount:N0}");
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
        finally
        {
            _sessions.SampleUpdated -= OnSample;
            _sessions.FrameTimesUpdated -= OnFrames;
        }

        GameSessionSample[] data;
        double[] raw;
        lock (gate)
        {
            data = samples.ToArray();
            raw = frameTimes.ToArray();
        }

        if (data.Length < 5)
            throw new InvalidOperationException("لم تصل Telemetry كافية من Game Session. تأكد أن اللعبة ما زالت شغالة وأن PresentMon يعمل.");

        var sorted = raw.OrderBy(x => x).ToArray();
        var aggregatedFps = data.Where(x => x.Fps is > 0).Select(x => x.Fps!.Value).ToArray();
        var aggregatedLows = data.Where(x => x.OnePercentLow is > 0).Select(x => x.OnePercentLow!.Value).ToArray();
        var aggregatedP99 = data.Where(x => x.P99FrameMs is > 0).Select(x => x.P99FrameMs!.Value).ToArray();
        var ping = data.Where(x => x.PingMs is > 0).Select(x => x.PingMs!.Value).ToArray();

        double? fps = null;
        double? oneLow = null;
        double? pointOneLow = null;
        double? p95 = null;
        double? p99 = null;
        double? p999 = null;
        double? maxFrame = null;
        double? cv = null;
        string quality;

        if (sorted.Length >= 120)
        {
            var avgFrame = raw.Average();
            p95 = Percentile(sorted, .95);
            p99 = Percentile(sorted, .99);
            p999 = Percentile(sorted, .999);
            maxFrame = sorted[^1];
            fps = avgFrame > 0 ? 1000d / avgFrame : null;
            oneLow = p99 > 0 ? 1000d / p99 : null;
            pointOneLow = p999 > 0 ? 1000d / p999 : null;
            var variance = raw.Sum(x => Math.Pow(x - avgFrame, 2)) / raw.Length;
            cv = avgFrame > 0 ? Math.Sqrt(variance) * 100d / avgFrame : null;
            quality = sorted.Length >= 1000 ? "Raw PresentMon / Strong sample" : "Raw PresentMon / Short sample";
        }
        else
        {
            fps = aggregatedFps.Length > 0 ? aggregatedFps.Average() : null;
            oneLow = aggregatedLows.Length > 0 ? aggregatedLows.Average() : null;
            p99 = aggregatedP99.Length > 0 ? aggregatedP99.Average() : null;
            maxFrame = aggregatedP99.Length > 0 ? aggregatedP99.Max() : null;
            quality = "Aggregated fallback — raw frame sample insufficient";
        }

        var id = $"{start:yyyyMMdd-HHmmss}-{SanitizeFile(game)}-{SanitizeFile(label)}";
        var path = Path.Combine(_root, id + ".json");
        var snapshot = new BenchmarkSnapshot(
            id,
            label,
            game,
            start,
            (DateTimeOffset.Now - start).TotalSeconds,
            data.Length,
            fps,
            oneLow,
            p99,
            maxFrame,
            data.Average(x => x.CpuLoad),
            data.Average(x => x.GpuLoad),
            data.Max(x => x.CpuTemp),
            data.Max(x => x.GpuTemp),
            data.Average(x => x.RamLoad),
            ping.Length > 0 ? ping.Average() : null,
            path,
            raw.LongLength,
            pointOneLow,
            p95,
            p999,
            maxFrame,
            cv,
            quality);

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
        var pointOne = PercentDelta(baseline.PointOnePercentLow, candidate.PointOnePercentLow);
        var p99Raw = PercentDelta(baseline.AverageP99FrameMs, candidate.AverageP99FrameMs);
        double? p99 = p99Raw.HasValue ? -p99Raw.Value : null; // lower frametime = positive improvement
        var cpu = candidate.AverageCpuLoad - baseline.AverageCpuLoad;
        var gpu = candidate.AverageGpuLoad - baseline.AverageGpuLoad;
        var cpuTemp = candidate.MaxCpuTemp - baseline.MaxCpuTemp;
        var gpuTemp = candidate.MaxGpuTemp - baseline.MaxGpuTemp;

        var sameGame = baseline.Game.Equals(candidate.Game, StringComparison.OrdinalIgnoreCase);
        var durationRatio = Math.Min(baseline.DurationSeconds, candidate.DurationSeconds) /
                            Math.Max(1, Math.Max(baseline.DurationSeconds, candidate.DurationSeconds));
        var rawQuality = baseline.FrameCount >= 300 && candidate.FrameCount >= 300;

        var weighted = WeightedScore(fps, low, pointOne, p99);
        var confidence = !sameGame ? "Invalid"
            : rawQuality && durationRatio >= .85 && Math.Min(baseline.DurationSeconds, candidate.DurationSeconds) >= 20 ? "High"
            : rawQuality && durationRatio >= .70 ? "Medium"
            : "Low";

        string verdict;
        if (!sameGame)
        {
            verdict = "INVALID A/B • الاختباران من لعبتين مختلفتين؛ D7KT يرفض استنتاج تحسين من هذه المقارنة.";
        }
        else if (!weighted.HasValue)
        {
            verdict = "INSUFFICIENT DATA • لا توجد Metrics مشتركة كافية للحكم.";
        }
        else
        {
            verdict = weighted.Value switch
            {
                >= 4 => "KEEP • تحسن واضح في مزيج FPS/1%/0.1%/P99.",
                >= 1.5 => "LIKELY KEEP • تحسن صغير قابل للقياس؛ كرر A/B إذا Confidence ليست High.",
                <= -4 => "REJECT • تراجع واضح؛ لا تعتمد التغيير.",
                <= -1.5 => "LIKELY REJECT • تراجع قابل للقياس؛ كرر الاختبار قبل الحكم النهائي.",
                _ => "NO PROOF • الفرق صغير؛ لا يوجد دليل كافٍ أن التغيير أفضل من baseline."
            };
        }

        if (cpuTemp >= 5 || gpuTemp >= 5)
            verdict += $" Thermal note: CPU {cpuTemp:+0.0;-0.0;0}°C • GPU {gpuTemp:+0.0;-0.0;0}°C.";
        verdict += $" Confidence={confidence}.";

        return new BenchmarkComparison(
            baseline,
            candidate,
            fps,
            low,
            p99,
            cpu,
            gpu,
            cpuTemp,
            gpuTemp,
            verdict,
            pointOne,
            weighted,
            confidence);
    }

    private static double? WeightedScore(double? fps, double? low, double? pointOne, double? p99)
    {
        var parts = new List<(double Value, double Weight)>();
        if (fps.HasValue) parts.Add((fps.Value, .20));
        if (low.HasValue) parts.Add((low.Value, .35));
        if (pointOne.HasValue) parts.Add((pointOne.Value, .20));
        if (p99.HasValue) parts.Add((p99.Value, .25));
        if (parts.Count == 0) return null;
        var weight = parts.Sum(x => x.Weight);
        return parts.Sum(x => x.Value * x.Weight) / weight;
    }

    private static double? PercentDelta(double? baseline, double? candidate)
    {
        if (!baseline.HasValue || !candidate.HasValue || Math.Abs(baseline.Value) < .0001) return null;
        return (candidate.Value - baseline.Value) * 100d / baseline.Value;
    }

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return 0;
        var index = Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1);
        return sorted[index];
    }

    private static string SanitizeLabel(string value)
        => string.IsNullOrWhiteSpace(value) ? "Benchmark" : value.Trim().Length > 80 ? value.Trim()[..80] : value.Trim();

    private static string SanitizeFile(string text)
        => string.Concat((text ?? string.Empty).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
