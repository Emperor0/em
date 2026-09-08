using D7.Benchmark.Capture;
using D7.Benchmark.Comparison;
using D7.Benchmark.Models;
using D7.Core.Logging;
using D7.Games.Detection;
using D7.Optimization.Contracts;
using D7.Orchestration.Models;
using D7.Rollback.Journal;
using D7.Rollback.Models;
using D7.Stability;

namespace D7.Orchestration;

public sealed class CoreFlowCoordinator
{
    private readonly IActiveGameDetector _games;
    private readonly IFrameCaptureService _frames;
    private readonly TransactionJournal _journal;
    private readonly JsonLineLogger _logger;
    private readonly IStabilityProbe _stability;

    public CoreFlowCoordinator(
        IActiveGameDetector games,
        IFrameCaptureService frames,
        TransactionJournal journal,
        JsonLineLogger logger,
        IStabilityProbe? stability = null)
    {
        _games = games;
        _frames = frames;
        _journal = journal;
        _logger = logger;
        _stability = stability ?? new NullStabilityProbe();
    }

    public Task<ActiveGame?> DetectGameAsync(CancellationToken cancellationToken) =>
        _games.DetectActiveGameAsync(cancellationToken);

    public async Task<BaselineFlowResult> CaptureBaselineAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        var game = await _games.DetectActiveGameAsync(cancellationToken).ConfigureAwait(false);
        if (game is null)
            return new BaselineFlowResult(false, null, null, "لم يكتشف D7 لعبة نشطة موثوقة.");

        return await CaptureBaselineForGameAsync(game, duration, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OptimizationExperimentResult> RunExperimentAsync(
        IReversibleOptimizationOperation operation,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var operationId = $"EXP-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        var game = await _games.DetectActiveGameAsync(cancellationToken).ConfigureAwait(false);
        if (game is null)
        {
            return new OptimizationExperimentResult(
                false,
                null,
                null,
                null,
                null,
                null,
                false,
                "لم يبدأ D7 أي تعديل لأنه لم يكتشف لعبة نشطة موثوقة.");
        }

        var context = new OperationContext(game.ProcessId, game.ProcessName, game.ExecutablePath);
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
            await _logger.WriteAsync(
                "Preflight",
                operationId,
                "Warning",
                "فشل فحص ما قبل التنفيذ وتم تجاوز التعديل.",
                new { operation.Id, ex.Message },
                CancellationToken.None);
            return new OptimizationExperimentResult(
                false,
                game,
                null,
                null,
                null,
                null,
                false,
                "تعذر التحقق من صلاحية التعديل قبل التنفيذ؛ لم يغير D7 أي إعداد.",
                ex.Message);
        }

        if (!preflight.Applicable)
        {
            await _logger.WriteAsync(
                "Preflight",
                operationId,
                "Information",
                "تم تجاوز تعديل غير قابل للتطبيق.",
                new { operation.Id, preflight.MessageAr, preflight.TechnicalReason },
                CancellationToken.None);
            return new OptimizationExperimentResult(
                true,
                game,
                null,
                null,
                null,
                null,
                false,
                preflight.MessageAr);
        }

        var baselineFlow = await CaptureBaselineForGameAsync(game, duration, cancellationToken).ConfigureAwait(false);
        if (!baselineFlow.Success || baselineFlow.Baseline?.Analysis is null)
        {
            return new OptimizationExperimentResult(
                false,
                game,
                baselineFlow.Baseline,
                null,
                null,
                null,
                false,
                "لم يبدأ D7 أي تعديل لأن القياس الأساسي غير صالح.");
        }

        OptimizationTransaction? transaction = null;
        CapturedOperationState? captured = null;
        var rolledBack = false;

        try
        {
            var stabilityBefore = await _stability.CaptureAsync(cancellationToken).ConfigureAwait(false);
            captured = await operation.CaptureStateAsync(context, cancellationToken).ConfigureAwait(false);
            transaction = await _journal.BeginAsync($"تجربة A/B: {operation.NameAr}", cancellationToken).ConfigureAwait(false);
            transaction = await _journal.UpsertOperationAsync(
                transaction,
                new OperationJournalEntry(
                    operation.Id,
                    captured.Kind,
                    captured.Target,
                    captured.BeforeJson,
                    null,
                    Applied: false,
                    Verified: false,
                    RolledBack: false,
                    Error: null,
                    UpdatedAt: DateTimeOffset.UtcNow),
                TransactionStatus.Started,
                cancellationToken).ConfigureAwait(false);

            var apply = await operation.ApplyAsync(context, cancellationToken).ConfigureAwait(false);
            transaction = await _journal.UpsertOperationAsync(
                transaction,
                new OperationJournalEntry(
                    operation.Id,
                    captured.Kind,
                    captured.Target,
                    captured.BeforeJson,
                    apply.AfterJson,
                    apply.Applied,
                    apply.Verified,
                    RolledBack: false,
                    apply.Error,
                    DateTimeOffset.UtcNow),
                apply.Verified ? TransactionStatus.Applying : TransactionStatus.Failed,
                cancellationToken).ConfigureAwait(false);

            if (!apply.Verified)
            {
                rolledBack = await operation.RollbackAsync(context, captured, cancellationToken).ConfigureAwait(false);
                transaction = await _journal.SetStatusAsync(
                    transaction,
                    rolledBack ? TransactionStatus.RolledBack : TransactionStatus.Failed,
                    apply.Error ?? "Apply verification failed",
                    cancellationToken).ConfigureAwait(false);
                return new OptimizationExperimentResult(
                    false, game, baselineFlow.Baseline, null, null, transaction.TransactionId, rolledBack,
                    "تعذر التحقق من التعديل، لذلك لم يُعتمد.", apply.Error);
            }

            if (!apply.Applied)
            {
                transaction = await _journal.SetStatusAsync(transaction, TransactionStatus.Committed, null, cancellationToken).ConfigureAwait(false);
                return new OptimizationExperimentResult(
                    true, game, baselineFlow.Baseline, null, null, transaction.TransactionId, false,
                    apply.MessageAr);
            }

            var candidate = await _frames.CaptureAsync(game.ProcessId, duration, cancellationToken).ConfigureAwait(false);
            if (!candidate.Success || candidate.Analysis is null || !candidate.Analysis.Valid)
            {
                rolledBack = await operation.RollbackAsync(context, captured, cancellationToken).ConfigureAwait(false);
                transaction = await MarkRollbackAsync(transaction, operation, captured, apply, rolledBack, "Candidate measurement invalid", cancellationToken).ConfigureAwait(false);
                return new OptimizationExperimentResult(
                    false, game, baselineFlow.Baseline, candidate, null, transaction.TransactionId, rolledBack,
                    "القياس بعد التعديل غير صالح، لذلك عاد D7 إلى الحالة السابقة.");
            }

            var stabilityAfterCandidate = await _stability.CaptureAsync(cancellationToken).ConfigureAwait(false);
            var candidateStability = StabilityComparer.Compare(stabilityBefore, stabilityAfterCandidate);
            if (candidateStability.Available && candidateStability.NewIssueCount > 0)
            {
                await _logger.WriteAsync(
                    "Stability",
                    operationId,
                    "Warning",
                    "ظهرت أحداث ثبات جديدة بعد التعديل.",
                    new { operation.Id, candidateStability.NewIssueCount, candidateStability.NewCriticalCount, candidateStability.NewIssues },
                    CancellationToken.None);
            }

            var comparison = BenchmarkComparator.Compare(
                baselineFlow.Baseline.Analysis,
                candidate.Analysis,
                baselineStabilityIssues: 0,
                candidateStabilityIssues: candidateStability.Available ? candidateStability.NewIssueCount : 0);

            if (comparison.Verdict == BenchmarkVerdict.Keep)
            {
                await _logger.WriteAsync(
                    "CoreFlow",
                    operationId,
                    "Information",
                    "المرور الأول يشير إلى تحسن. بدء قياس تأكيد قبل الاعتماد.",
                    new { operation.Id, comparison },
                    CancellationToken.None);

                var confirmation = await _frames.CaptureAsync(game.ProcessId, duration, cancellationToken).ConfigureAwait(false);
                if (!confirmation.Success || confirmation.Analysis is null || !confirmation.Analysis.Valid)
                {
                    rolledBack = await operation.RollbackAsync(context, captured, cancellationToken).ConfigureAwait(false);
                    transaction = await MarkRollbackAsync(transaction, operation, captured, apply, rolledBack, "Confirmation measurement invalid", cancellationToken).ConfigureAwait(false);
                    return new OptimizationExperimentResult(
                        false,
                        game,
                        baselineFlow.Baseline,
                        candidate,
                        comparison,
                        transaction.TransactionId,
                        rolledBack,
                        "تحسن القياس الأول، لكن قياس التأكيد لم يكن صالحًا؛ لذلك عاد D7 للحالة الأصلية.",
                        Confirmation: confirmation);
                }

                var stabilityAfterConfirmation = await _stability.CaptureAsync(cancellationToken).ConfigureAwait(false);
                var confirmationStability = StabilityComparer.Compare(stabilityBefore, stabilityAfterConfirmation);
                if (confirmationStability.Available && confirmationStability.NewIssueCount > 0)
                {
                    await _logger.WriteAsync(
                        "Stability",
                        operationId,
                        "Warning",
                        "قياس التأكيد رصد أحداث ثبات جديدة.",
                        new { operation.Id, confirmationStability.NewIssueCount, confirmationStability.NewCriticalCount, confirmationStability.NewIssues },
                        CancellationToken.None);
                }

                var confirmationComparison = BenchmarkComparator.Compare(
                    baselineFlow.Baseline.Analysis,
                    confirmation.Analysis,
                    baselineStabilityIssues: 0,
                    candidateStabilityIssues: confirmationStability.Available ? confirmationStability.NewIssueCount : 0);
                if (confirmationComparison.Verdict == BenchmarkVerdict.Keep)
                {
                    transaction = await _journal.SetStatusAsync(transaction, TransactionStatus.Committed, null, cancellationToken).ConfigureAwait(false);
                    await _logger.WriteAsync(
                        "CoreFlow",
                        operationId,
                        "Information",
                        "تم الاحتفاظ بالتعديل بعد نجاح قياسي التحسن والتأكيد دون أحداث ثبات جديدة.",
                        new { operation.Id, comparison, confirmationComparison },
                        CancellationToken.None);
                    return new OptimizationExperimentResult(
                        true,
                        game,
                        baselineFlow.Baseline,
                        candidate,
                        confirmationComparison,
                        transaction.TransactionId,
                        false,
                        "أثبت القياس ثم قياس التأكيد تحسنًا متكررًا دون مشاكل ثبات جديدة؛ تم اعتماد التعديل.",
                        Confirmation: confirmation,
                        ConfirmationComparison: confirmationComparison);
                }

                rolledBack = await operation.RollbackAsync(context, captured, cancellationToken).ConfigureAwait(false);
                transaction = await MarkRollbackAsync(
                    transaction,
                    operation,
                    captured,
                    apply,
                    rolledBack,
                    confirmationStability.Available && confirmationStability.NewIssueCount > 0
                        ? "Stability issue detected during confirmation"
                        : "Confirmation did not reproduce improvement",
                    cancellationToken).ConfigureAwait(false);
                return new OptimizationExperimentResult(
                    true,
                    game,
                    baselineFlow.Baseline,
                    candidate,
                    confirmationComparison,
                    transaction.TransactionId,
                    rolledBack,
                    confirmationStability.Available && confirmationStability.NewIssueCount > 0
                        ? "ظهر حدث ثبات جديد أثناء الاختبار؛ لذلك استعاد D7 الحالة الأصلية."
                        : "التحسن لم يتكرر في قياس التأكيد؛ لذلك استعاد D7 الحالة الأصلية.",
                    Confirmation: confirmation,
                    ConfirmationComparison: confirmationComparison);
            }

            rolledBack = await operation.RollbackAsync(context, captured, cancellationToken).ConfigureAwait(false);
            transaction = await MarkRollbackAsync(
                transaction,
                operation,
                captured,
                apply,
                rolledBack,
                candidateStability.Available && candidateStability.NewIssueCount > 0
                    ? "Stability issue detected"
                    : comparison.Verdict == BenchmarkVerdict.Rollback ? "Performance regression" : "Inconclusive result",
                cancellationToken).ConfigureAwait(false);

            var message = candidateStability.Available && candidateStability.NewIssueCount > 0
                ? "ظهر حدث ثبات جديد بعد التعديل، لذلك استعاد D7 الإعداد السابق فورًا."
                : comparison.Verdict == BenchmarkVerdict.Rollback
                    ? "أظهر القياس تراجعًا، لذلك استعاد D7 الإعداد السابق."
                    : "الفرق غير حاسم، لذلك اختار D7 الحالة الأصلية الأكثر أمانًا.";
            return new OptimizationExperimentResult(
                true, game, baselineFlow.Baseline, candidate, comparison, transaction.TransactionId, rolledBack, message);
        }
        catch (OperationCanceledException)
        {
            if (transaction is not null && captured is not null)
            {
                rolledBack = await operation.RollbackAsync(context, captured, CancellationToken.None).ConfigureAwait(false);
                _ = await _journal.SetStatusAsync(
                    transaction,
                    rolledBack ? TransactionStatus.RolledBack : TransactionStatus.Failed,
                    "Canceled",
                    CancellationToken.None).ConfigureAwait(false);
            }
            throw;
        }
        catch (Exception ex)
        {
            if (transaction is not null && captured is not null)
            {
                rolledBack = await operation.RollbackAsync(context, captured, CancellationToken.None).ConfigureAwait(false);
                transaction = await _journal.SetStatusAsync(
                    transaction,
                    rolledBack ? TransactionStatus.RolledBack : TransactionStatus.Failed,
                    ex.Message,
                    CancellationToken.None).ConfigureAwait(false);
            }

            await _logger.WriteAsync("CoreFlow", operationId, "Error", "فشل مسار التجربة وتم تنفيذ التراجع عند الإمكان.", new { ex.Message }, CancellationToken.None);
            return new OptimizationExperimentResult(
                false, game, baselineFlow.Baseline, null, null, transaction?.TransactionId, rolledBack,
                "حدث خطأ أثناء التجربة ولم يعتمد D7 التعديل.", ex.Message);
        }
    }

    private async Task<BaselineFlowResult> CaptureBaselineForGameAsync(
        ActiveGame game,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var baseline = await _frames.CaptureAsync(game.ProcessId, duration, cancellationToken).ConfigureAwait(false);
        if (!baseline.Success || baseline.Analysis is null || !baseline.Analysis.Valid)
            return new BaselineFlowResult(false, game, baseline, baseline.MessageAr);

        return new BaselineFlowResult(true, game, baseline, "اكتمل القياس الأساسي وحُفظت بيانات الإطارات قبل أي تعديل.");
    }

    private async Task<OptimizationTransaction> MarkRollbackAsync(
        OptimizationTransaction transaction,
        IReversibleOptimizationOperation operation,
        CapturedOperationState captured,
        OperationApplyResult apply,
        bool rolledBack,
        string reason,
        CancellationToken cancellationToken)
    {
        transaction = await _journal.UpsertOperationAsync(
            transaction,
            new OperationJournalEntry(
                operation.Id,
                captured.Kind,
                captured.Target,
                captured.BeforeJson,
                apply.AfterJson,
                apply.Applied,
                apply.Verified,
                rolledBack,
                rolledBack ? null : reason,
                DateTimeOffset.UtcNow),
            TransactionStatus.RollingBack,
            cancellationToken).ConfigureAwait(false);

        return await _journal.SetStatusAsync(
            transaction,
            rolledBack ? TransactionStatus.RolledBack : TransactionStatus.Failed,
            rolledBack ? null : reason,
            cancellationToken).ConfigureAwait(false);
    }
}
