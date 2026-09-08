using D7.Core.Foundation;
using D7.Core.Logging;
using D7.Optimization.Contracts;
using D7.Orchestration;
using D7.Rollback.Journal;
using D7.Rollback.Models;
using Xunit;

namespace D7.UnitTests;

public sealed class StartupRecoveryTests
{
    [Fact]
    public async Task AppliedIncompleteOperation_IsRolledBackOnStartup()
    {
        var root = Path.Combine(Path.GetTempPath(), "D7-Recovery-Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(Path.Combine(root, "pd"), Path.Combine(root, "la"));
            paths.EnsureCreated();
            var logger = new JsonLineLogger(paths);
            var journal = new TransactionJournal(paths);
            var operation = new FakeRecoveryOperation();

            var transaction = await journal.BeginAsync("test", CancellationToken.None);
            transaction = await journal.UpsertOperationAsync(
                transaction,
                new OperationJournalEntry(
                    operation.Id,
                    "test",
                    "777",
                    "{\"value\":\"before\"}",
                    "{\"value\":\"after\"}",
                    Applied: true,
                    Verified: true,
                    RolledBack: false,
                    Error: null,
                    UpdatedAt: DateTimeOffset.UtcNow),
                TransactionStatus.Applying,
                CancellationToken.None);

            var recovery = new StartupRecoveryService(journal, [operation], logger);
            var result = await recovery.RecoverAsync(CancellationToken.None);

            Assert.Equal(1, result.TransactionsFound);
            Assert.Equal(1, result.TransactionsRecovered);
            Assert.Equal(1, result.OperationsRecovered);
            Assert.Equal(0, result.OperationsUnresolved);
            Assert.Equal(1, operation.RollbackCalls);
            Assert.Empty(await journal.FindIncompleteAsync(CancellationToken.None));
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public async Task UnknownAppliedOperation_RemainsVisibleAsUnresolved()
    {
        var root = Path.Combine(Path.GetTempPath(), "D7-Recovery-Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(Path.Combine(root, "pd"), Path.Combine(root, "la"));
            paths.EnsureCreated();
            var logger = new JsonLineLogger(paths);
            var journal = new TransactionJournal(paths);

            var transaction = await journal.BeginAsync("test", CancellationToken.None);
            _ = await journal.UpsertOperationAsync(
                transaction,
                new OperationJournalEntry(
                    "unknown.operation",
                    "test",
                    "1",
                    "{}",
                    "{}",
                    Applied: true,
                    Verified: true,
                    RolledBack: false,
                    Error: null,
                    UpdatedAt: DateTimeOffset.UtcNow),
                TransactionStatus.Applying,
                CancellationToken.None);

            var recovery = new StartupRecoveryService(journal, Array.Empty<IReversibleOptimizationOperation>(), logger);
            var result = await recovery.RecoverAsync(CancellationToken.None);

            Assert.Equal(1, result.OperationsUnresolved);
            Assert.Single(await journal.FindIncompleteAsync(CancellationToken.None));
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    private sealed class FakeRecoveryOperation : IReversibleOptimizationOperation
    {
        public string Id => "test.recovery";
        public string NameAr => "اختبار استعادة";
        public string Risk => "LOW";
        public int RollbackCalls { get; private set; }

        public Task<CapturedOperationState> CaptureStateAsync(OperationContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new CapturedOperationState("test", context.ProcessId.ToString(), "{}"));

        public Task<OperationApplyResult> ApplyAsync(OperationContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new OperationApplyResult(true, true, "{}", "ok"));

        public Task<bool> RollbackAsync(OperationContext context, CapturedOperationState capturedState, CancellationToken cancellationToken)
        {
            RollbackCalls++;
            return Task.FromResult(true);
        }
    }
}
