using System.Text.Json;
using D7.Core.Foundation;
using D7.Rollback.Models;

namespace D7.Rollback.Journal;

public sealed class TransactionJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public TransactionJournal(AppPaths paths) => _paths = paths;

    public async Task<OptimizationTransaction> BeginAsync(string reasonAr, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var tx = new OptimizationTransaction(
            $"TX-{now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}",
            now,
            now,
            TransactionStatus.Started,
            reasonAr,
            Array.Empty<OperationJournalEntry>());
        await SaveAsync(tx, cancellationToken).ConfigureAwait(false);
        return tx;
    }

    public async Task<OptimizationTransaction> UpsertOperationAsync(
        OptimizationTransaction transaction,
        OperationJournalEntry operation,
        TransactionStatus status,
        CancellationToken cancellationToken)
    {
        var operations = transaction.Operations
            .Where(x => !string.Equals(x.OperationId, operation.OperationId, StringComparison.Ordinal))
            .Append(operation)
            .ToArray();

        var updated = transaction with
        {
            Operations = operations,
            Status = status,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<OptimizationTransaction> SetStatusAsync(
        OptimizationTransaction transaction,
        TransactionStatus status,
        string? error,
        CancellationToken cancellationToken)
    {
        var updated = transaction with
        {
            Status = status,
            Error = error,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<IReadOnlyList<OptimizationTransaction>> FindIncompleteAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.Transactions);
        var result = new List<OptimizationTransaction>();

        foreach (var file in Directory.EnumerateFiles(_paths.Transactions, "TX-*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = File.OpenRead(file);
                var tx = await JsonSerializer.DeserializeAsync<OptimizationTransaction>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
                if (tx is not null && tx.Status is not TransactionStatus.Committed and not TransactionStatus.RolledBack)
                    result.Add(tx);
            }
            catch
            {
                // Corrupt journals are preserved for diagnostics instead of silently deleted.
            }
        }

        return result.OrderBy(x => x.StartedAt).ToArray();
    }

    public string GetPath(string transactionId) => Path.Combine(_paths.Transactions, transactionId + ".json");

    private async Task SaveAsync(OptimizationTransaction transaction, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_paths.Transactions);
            var target = GetPath(transaction.TransactionId);
            var temp = target + ".tmp";

            await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                await JsonSerializer.SerializeAsync(stream, transaction, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temp, target, true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
