using D7.Benchmark.Models;
using D7.Games.Detection;
using D7.Optimization.Contracts;
using D7.Orchestration.Models;

namespace D7.Orchestration.Learning;

public sealed class OptimizationLearningService
{
    private readonly OptimizationHistoryStore _store;

    public OptimizationLearningService(OptimizationHistoryStore store)
    {
        _store = store;
    }

    public Task RecordAsync(
        ActiveGame game,
        IReversibleOptimizationOperation operation,
        OptimizationExperimentResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(result);

        var comparison = result.ConfirmationComparison ?? result.Comparison;
        var outcome = ResolveOutcome(result, comparison);
        var reasons = comparison?.Reasons ?? Array.Empty<string>();

        return _store.AppendAsync(
            new OptimizationHistoryRecord(
                game.ProcessName,
                game.ExecutablePath,
                operation.Id,
                operation.NameAr,
                outcome,
                DateTimeOffset.UtcNow,
                comparison?.OnePercentLowDeltaPercent,
                comparison?.P99DeltaPercent,
                comparison?.StutterDelta,
                result.MessageAr,
                reasons),
            cancellationToken);
    }

    public static OptimizationHistoryOutcome ResolveOutcome(
        OptimizationExperimentResult result,
        BenchmarkComparison? comparison)
    {
        if (result.RolledBack)
        {
            return comparison?.Verdict == BenchmarkVerdict.Inconclusive
                ? OptimizationHistoryOutcome.Inconclusive
                : OptimizationHistoryOutcome.RolledBack;
        }

        if (comparison?.Verdict == BenchmarkVerdict.Keep)
            return OptimizationHistoryOutcome.Kept;

        if (comparison?.Verdict == BenchmarkVerdict.Inconclusive)
            return OptimizationHistoryOutcome.Inconclusive;

        if (!result.Success)
            return OptimizationHistoryOutcome.Failed;

        if (result.Baseline is null && result.Candidate is null && result.TransactionId is null)
            return OptimizationHistoryOutcome.NoOp;

        return OptimizationHistoryOutcome.Failed;
    }
}
