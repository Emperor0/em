using D7.Core.Foundation;
using D7.Rollback.Journal;
using D7.Rollback.Models;
using Xunit;

namespace D7.UnitTests;

public sealed class TransactionJournalTests
{
    [Fact]
    public async Task IncompleteTransaction_IsRecoveredByDiscovery()
    {
        var root = Path.Combine(Path.GetTempPath(), "D7Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(Path.Combine(root, "pd"), Path.Combine(root, "la"));
            var journal = new TransactionJournal(paths);
            var tx = await journal.BeginAsync("اختبار", CancellationToken.None);
            var operation = new OperationJournalEntry(
                "op1", "registry", "test", "{\"v\":0}", "{\"v\":1}",
                true, true, false, null, DateTimeOffset.UtcNow);
            tx = await journal.UpsertOperationAsync(tx, operation, TransactionStatus.Applying, CancellationToken.None);

            var incomplete = await journal.FindIncompleteAsync(CancellationToken.None);

            var found = Assert.Single(incomplete);
            Assert.Equal(tx.TransactionId, found.TransactionId);
            Assert.Equal(TransactionStatus.Applying, found.Status);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task CommittedTransaction_IsNotReportedIncomplete()
    {
        var root = Path.Combine(Path.GetTempPath(), "D7Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(Path.Combine(root, "pd"), Path.Combine(root, "la"));
            var journal = new TransactionJournal(paths);
            var tx = await journal.BeginAsync("اختبار", CancellationToken.None);
            _ = await journal.SetStatusAsync(tx, TransactionStatus.Committed, null, CancellationToken.None);

            var incomplete = await journal.FindIncompleteAsync(CancellationToken.None);
            Assert.Empty(incomplete);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
