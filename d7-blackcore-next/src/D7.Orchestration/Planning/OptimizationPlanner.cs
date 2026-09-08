using D7.Games.Detection;
using D7.Optimization.Contracts;

namespace D7.Orchestration.Planning;

public sealed record OptimizationCandidateCheck(
    string OperationId,
    string NameAr,
    string Risk,
    bool Applicable,
    string MessageAr,
    string? TechnicalReason);

public sealed record OptimizationPlanDecision(
    IReversibleOptimizationOperation? Operation,
    IReadOnlyList<OptimizationCandidateCheck> Checks,
    string MessageAr)
{
    public bool HasOperation => Operation is not null;
}

public sealed class OptimizationPlanner
{
    private readonly IReadOnlyList<IReversibleOptimizationOperation> _operations;

    public OptimizationPlanner(IEnumerable<IReversibleOptimizationOperation> operations)
    {
        _operations = operations?.ToArray() ?? throw new ArgumentNullException(nameof(operations));
        if (_operations.Count == 0) throw new ArgumentException("At least one operation is required.", nameof(operations));
    }

    public async Task<OptimizationPlanDecision> SelectNextAsync(ActiveGame game, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        var context = new OperationContext(game.ProcessId, game.ProcessName, game.ExecutablePath);
        var checks = new List<OptimizationCandidateCheck>(_operations.Count);

        foreach (var operation in _operations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.Equals(operation.Risk, "LOW", StringComparison.OrdinalIgnoreCase))
            {
                checks.Add(new OptimizationCandidateCheck(
                    operation.Id,
                    operation.NameAr,
                    operation.Risk,
                    false,
                    "تم استبعاد العملية من الوضع التلقائي لأن مستوى المخاطر ليس LOW.",
                    "Automatic planner only permits LOW-risk operations."));
                continue;
            }

            OperationPreflightResult preflight;
            try
            {
                preflight = await operation.PreflightAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                preflight = new OperationPreflightResult(
                    false,
                    "فشل فحص ما قبل التنفيذ، لذلك تم تجاوز العملية بأمان.",
                    ex.Message);
            }

            checks.Add(new OptimizationCandidateCheck(
                operation.Id,
                operation.NameAr,
                operation.Risk,
                preflight.Applicable,
                preflight.MessageAr,
                preflight.TechnicalReason));

            if (preflight.Applicable)
            {
                return new OptimizationPlanDecision(
                    operation,
                    checks,
                    $"اختار D7 التجربة التالية بناءً على قابلية التطبيق: {operation.NameAr}.");
            }
        }

        return new OptimizationPlanDecision(
            null,
            checks,
            "لا توجد حاليًا تجربة منخفضة المخاطر تنطبق على هذه اللعبة والجهاز؛ لم يغير D7 أي إعداد.");
    }
}
