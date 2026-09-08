using D7.Core.Logging;
using D7.Optimization.Contracts;
using D7.Rollback.Journal;
using D7.Rollback.Models;

namespace D7.Orchestration;

public sealed record StartupRecoveryResult(
    int TransactionsFound,
    int TransactionsRecovered,
    int OperationsRecovered,
    int OperationsUnresolved,
    string MessageAr);

public sealed class StartupRecoveryService
{
    private readonly TransactionJournal _journal;
    private readonly IReadOnlyDictionary<string, IReversibleOptimizationOperation> _operations;
    private readonly JsonLineLogger _logger;

    public StartupRecoveryService(
        TransactionJournal journal,
        IEnumerable<IReversibleOptimizationOperation> operations,
        JsonLineLogger logger)
    {
        _journal = journal;
        _logger = logger;
        _operations = operations.ToDictionary(x => x.Id, StringComparer.Ordinal);
    }

    public async Task<StartupRecoveryResult> RecoverAsync(CancellationToken cancellationToken)
    {
        var incomplete = await _journal.FindIncompleteAsync(cancellationToken).ConfigureAwait(false);
        var recoveredTransactions = 0;
        var recoveredOperations = 0;
        var unresolvedOperations = 0;

        foreach (var original in incomplete)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transaction = await _journal.SetStatusAsync(
                original,
                TransactionStatus.RollingBack,
                original.Error,
                cancellationToken).ConfigureAwait(false);
            var allRecovered = true;

            foreach (var entry in transaction.Operations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.RolledBack || !entry.Applied)
                {
                    continue;
                }

                if (!_operations.TryGetValue(entry.OperationId, out var operation))
                {
                    unresolvedOperations++;
                    allRecovered = false;
                    continue;
                }

                if (!int.TryParse(entry.Target, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var processId))
                {
                    unresolvedOperations++;
                    allRecovered = false;
                    continue;
                }

                var captured = new CapturedOperationState(entry.Kind, entry.Target, entry.BeforeJson ?? "{}");
                var context = new OperationContext(processId, "recovery", null);
                var rolledBack = false;
                try
                {
                    rolledBack = await operation.RollbackAsync(context, captured, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await _logger.WriteAsync(
                        "Recovery",
                        transaction.TransactionId,
                        "Error",
                        "تعذر التراجع عن عملية غير مكتملة.",
                        new { entry.OperationId, entry.Target, ex.Message },
                        CancellationToken.None);
                }

                if (rolledBack)
                {
                    recoveredOperations++;
                }
                else
                {
                    unresolvedOperations++;
                    allRecovered = false;
                }

                transaction = await _journal.UpsertOperationAsync(
                    transaction,
                    entry with
                    {
                        RolledBack = rolledBack,
                        Error = rolledBack ? null : entry.Error ?? "Startup rollback failed",
                        UpdatedAt = DateTimeOffset.UtcNow
                    },
                    TransactionStatus.RollingBack,
                    cancellationToken).ConfigureAwait(false);
            }

            if (allRecovered)
            {
                transaction = await _journal.SetStatusAsync(transaction, TransactionStatus.RolledBack, null, cancellationToken).ConfigureAwait(false);
                recoveredTransactions++;
            }
            else
            {
                transaction = await _journal.SetStatusAsync(
                    transaction,
                    TransactionStatus.Failed,
                    "Startup recovery left unresolved operations.",
                    cancellationToken).ConfigureAwait(false);
            }
        }

        var message = incomplete.Count == 0
            ? "لا توجد عمليات سابقة غير مكتملة."
            : unresolvedOperations == 0
                ? $"استعاد D7 {recoveredTransactions} عملية غير مكتملة بأمان."
                : $"تمت استعادة {recoveredOperations} عملية، وتوجد {unresolvedOperations} عملية تحتاج مراجعة.";

        await _logger.WriteAsync(
            "Recovery",
            "STARTUP",
            unresolvedOperations == 0 ? "Information" : "Warning",
            message,
            new { TransactionsFound = incomplete.Count, recoveredTransactions, recoveredOperations, unresolvedOperations },
            CancellationToken.None);

        return new StartupRecoveryResult(
            incomplete.Count,
            recoveredTransactions,
            recoveredOperations,
            unresolvedOperations,
            message);
    }
}
