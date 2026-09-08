using D7.Benchmark.Capture;
using D7.Benchmark.Comparison;
using D7.Benchmark.Models;
using D7.Core.Logging;
using D7.Games.Detection;
using D7.Optimization.Contracts;
using D7.Orchestration.Models;
using D7.Rollback.Journal;
using D7.Rollback.Models;

namespace D7.Orchestration;

public sealed class CoreFlowCoordinator
{
    private readonly IActiveGameDetector _games;
    private readonly IFrameCaptureService _frames;
    private readonly TransactionJournal _journal;
    private readonly JsonLineLogger _logger;

    public CoreFlowCoordinator(
        IActiveGameDetector games,
        IFrameCaptureService frames,
        TransactionJournal journal,
        JsonLineLogger logger)
    {
        _games = games;
        _frames = frames;
        _journal = journal;
        _logger = logger;
    }

    public async Task<BaselineFlowResult> CaptureBaselineAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        var game = await _games.DetectActiveGameAsync(cancellationToken).ConfigureAwait(false);
        if (game is null)
            return new BaselineFlowResult(false, null, null, "لم يكتشف D7 لعبة نشطة موثوقة في الواجهة الأمامية.");

        var baseline = await _frames.CaptureAsync(game.ProcessId, duration, cancellationToken).ConfigureAwait(false);
        if (!baseline.Success || baseline.Analysis is null || !baseline.Analysis.Valid)
            return new BaselineFlowResult(false, game, baseline, baseline.MessageAr);

        return new BaselineFlowResult(true, game, baseline, "اكتمل القياس الأساسي وحُفظت بيانات الإطارات قبل أي تعديل.");
    }

    public async Task<OptimizationExperimentResult> RunExperimentAsync(
        IReversibleOptimizationOperation operation,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var operationId = $"EXP-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        var baselineFlow = await CaptureBaselineAsync(duration, cancellationToken).ConfigureAwait(false);
        if (!baselineFlow.Success || baselineFlow.Game is null || baselineFlow.Baseline?.Analysis is null)
        {
            return new OptimizationExperimentResult(
                false,
                baselineFlow.Game,
                baselineFlow.Baseline,
                null,
                null,
                null,
                false,
                "لم يبدأ D7 أي تعديل لأن القياس الأساسي غير صالح.");
        }

        var game = baselineFlow.Game;
        var context = new OperationContext(game.ProcessId, game.ProcessName, game.ExecutablePath);
        OptimizationTransaction? transaction = null;
        CapturedOperationState? captured = null;
        var rolledBack = false;

        try
        {
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

            var comparison = BenchmarkComparator.Compare(baselineFlow.Baseline.Analysis, candidate.Analysis);
            if (comparison.Verdict == BenchmarkVerdict.Keep)
            {
                transaction = await _journal.SetStatusAsync(transaction, TransactionStatus.Committed, null, cancellationToken).ConfigureAwait(false);
                await _logger.WriteAsync("CoreFlow", operationId, "Information", "تم الاحتفاظ بالتعديل بعد تحسن القياس.", new { operation.Id, comparison }, CancellationToken.None);
                return new OptimizationExperimentResult(
                    true, game, baselineFlow.Baseline, candidate, comparison, transaction.TransactionId, false,
                    "أثبت القياس تحسنًا كافيًا، لذلك تم الاحتفاظ بالتعديل.");
            }

            rolledBack = await operation.RollbackAsync(context, captured, cancellationToken).ConfigureAwait(false);
            transaction = await MarkRollbackAsync(
                transaction,
                operation,
                captured,
                apply,
                rolledBack,
                comparison.Verdict == BenchmarkVerdict.Rollback ? "Performance regression" : "Inconclusive result",
                cancellationToken).ConfigureAwait(false);

            var message = comparison.Verdict == BenchmarkVerdict.Rollback
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
