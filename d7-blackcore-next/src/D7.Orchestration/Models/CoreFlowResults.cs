using D7.Benchmark.Models;
using D7.Games.Detection;

namespace D7.Orchestration.Models;

public sealed record BaselineFlowResult(
    bool Success,
    ActiveGame? Game,
    BenchmarkCaptureResult? Baseline,
    string MessageAr);

public sealed record OptimizationExperimentResult(
    bool Success,
    ActiveGame? Game,
    BenchmarkCaptureResult? Baseline,
    BenchmarkCaptureResult? Candidate,
    BenchmarkComparison? Comparison,
    string? TransactionId,
    bool RolledBack,
    string MessageAr,
    string? Error = null,
    BenchmarkCaptureResult? Confirmation = null,
    BenchmarkComparison? ConfirmationComparison = null);
