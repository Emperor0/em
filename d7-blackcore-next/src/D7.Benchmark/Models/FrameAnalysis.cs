namespace D7.Benchmark.Models;

public sealed record FrameAnalysis(
    bool Valid,
    int FrameCount,
    double AverageFps,
    double OnePercentLowAverageFps,
    double ZeroPointOnePercentLowAverageFps,
    double AverageFrameTimeMs,
    double MedianFrameTimeMs,
    double P95FrameTimeMs,
    double P99FrameTimeMs,
    double P999FrameTimeMs,
    int StutterCount,
    double StutterTimePercent,
    string FrameTimeColumn,
    string MethodVersion,
    IReadOnlyList<string> Warnings);

public sealed record BenchmarkCaptureResult(
    bool Success,
    int ProcessId,
    DateTimeOffset StartedAt,
    TimeSpan RequestedDuration,
    string? CsvPath,
    FrameAnalysis? Analysis,
    string MessageAr,
    string? TechnicalError = null);

public enum BenchmarkVerdict
{
    Keep,
    Rollback,
    Inconclusive
}

public sealed record BenchmarkComparison(
    BenchmarkVerdict Verdict,
    string SummaryAr,
    double OnePercentLowDeltaPercent,
    double P99DeltaPercent,
    int StutterDelta,
    IReadOnlyList<string> Reasons);
