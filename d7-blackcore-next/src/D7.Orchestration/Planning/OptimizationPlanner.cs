using D7.Games.Detection;
using D7.Optimization.Contracts;
using D7.Orchestration.Learning;

namespace D7.Orchestration.Planning;

public sealed record OptimizationCandidateCheck(
    string OperationId,
    string NameAr,
    string Risk,
    bool Applicable,
    string MessageAr,
    string? TechnicalReason,
    int PriorityScore = 0,
    string? LearningNoteAr = null);

public sealed record OptimizationPlanDecision(
    IReversibleOptimizationOperation? Operation,
    IReadOnlyList<OptimizationCandidateCheck> Checks,
    string MessageAr)
{
    public bool HasOperation => Operation is not null;
}

public sealed class OptimizationPlanner
{
    private static readonly TimeSpan RecentFailureCooldown = TimeSpan.FromHours(12);
    private readonly IReadOnlyList<IReversibleOptimizationOperation> _operations;
    private readonly OptimizationHistoryStore? _history;

    public OptimizationPlanner(
        IEnumerable<IReversibleOptimizationOperation> operations,
        OptimizationHistoryStore? history = null)
    {
        _operations = operations?.ToArray() ?? throw new ArgumentNullException(nameof(operations));
        if (_operations.Count == 0) throw new ArgumentException("At least one operation is required.", nameof(operations));
        _history = history;
    }

    public async Task<OptimizationPlanDecision> SelectNextAsync(ActiveGame game, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        var context = new OperationContext(game.ProcessId, game.ProcessName, game.ExecutablePath);
        var checks = new List<OptimizationCandidateCheck>(_operations.Count);
        var candidates = new List<(IReversibleOptimizationOperation Operation, int Score)>();
        var history = _history is null
            ? Array.Empty<OptimizationHistoryRecord>()
            : (await _history.GetForGameAsync(game, cancellationToken).ConfigureAwait(false)).ToArray();
        var now = DateTimeOffset.UtcNow;

        for (var index = 0; index < _operations.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = _operations[index];

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

            var operationHistory = history
                .Where(x => string.Equals(x.OperationId, operation.Id, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.RecordedAt)
                .ToArray();
            var recentBad = operationHistory.FirstOrDefault(x =>
                now - x.RecordedAt <= RecentFailureCooldown &&
                x.Outcome is OptimizationHistoryOutcome.RolledBack or OptimizationHistoryOutcome.Inconclusive or OptimizationHistoryOutcome.Failed);

            if (recentBad is not null)
            {
                checks.Add(new OptimizationCandidateCheck(
                    operation.Id,
                    operation.NameAr,
                    operation.Risk,
                    false,
                    "تم تأجيل إعادة هذه التجربة لأن نتيجة حديثة لم تبرر الاحتفاظ بها.",
                    $"Recent outcome: {recentBad.Outcome} at {recentBad.RecordedAt:O}",
                    PriorityScore: -100,
                    LearningNoteAr: "D7 يتجنب تكرار تجربة فاشلة أو غير حاسمة خلال 12 ساعة."));
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

            var keepCount = operationHistory.Count(x => x.Outcome == OptimizationHistoryOutcome.Kept);
            var learningBoost = Math.Min(30, keepCount * 10);
            var score = 100 - index + learningBoost;
            var learningNote = keepCount > 0
                ? $"هذه التجربة أثبتت تحسنًا سابقًا لهذه اللعبة {keepCount} مرة؛ تم رفع أولويتها دون تجاوز القياس الجديد."
                : "لا توجد نتيجة إيجابية سابقة كافية لرفع أولوية هذه التجربة.";

            checks.Add(new OptimizationCandidateCheck(
                operation.Id,
                operation.NameAr,
                operation.Risk,
                preflight.Applicable,
                preflight.MessageAr,
                preflight.TechnicalReason,
                preflight.Applicable ? score : 0,
                learningNote));

            if (preflight.Applicable)
                candidates.Add((operation, score));
        }

        var selected = candidates
            .OrderByDescending(x => x.Score)
            .ThenBy(x => Array.IndexOf(_operations.ToArray(), x.Operation))
            .FirstOrDefault();

        if (selected.Operation is not null)
        {
            var selectedCheck = checks.First(x => string.Equals(x.OperationId, selected.Operation.Id, StringComparison.OrdinalIgnoreCase));
            return new OptimizationPlanDecision(
                selected.Operation,
                checks,
                $"اختار D7 التجربة التالية بعد فحص القابلية والتاريخ السابق: {selected.Operation.NameAr}. {selectedCheck.LearningNoteAr}");
        }

        return new OptimizationPlanDecision(
            null,
            checks,
            "لا توجد حاليًا تجربة منخفضة المخاطر تنطبق على هذه اللعبة والجهاز أو يسمح التاريخ القريب بإعادتها؛ لم يغير D7 أي إعداد.");
    }
}
