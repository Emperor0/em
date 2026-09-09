using D7.Benchmark.Confidence;

namespace D7.Benchmark.Models;

public sealed record MeasurementManifest(
    string Schema,
    DateTimeOffset CapturedAt,
    int ProcessId,
    double RequestedDurationSeconds,
    string WindowsVersion,
    string RuntimeVersion,
    string PresentMonVersion,
    string PresentMonSha256,
    string PresentMonAsset,
    string CsvFile,
    FrameAnalysis Analysis,
    BenchmarkConfidence? Confidence = null);
