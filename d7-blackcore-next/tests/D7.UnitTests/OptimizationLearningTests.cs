using D7.Benchmark.Models;
using D7.Core.Foundation;
using D7.Games.Detection;
using D7.Optimization.Contracts;
using D7.Orchestration.Learning;
using D7.Orchestration.Models;
using D7.Orchestration.Planning;
using Xunit;

namespace D7.UnitTests;

public sealed class OptimizationLearningTests
{
    [Fact]
    public async Task HistoryStore_PersistsAndReloadsGameOutcome()
    {
        var root = Path.Combine(Path.GetTempPath(), "D7-Learning-Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(Path.Combine(root, "pd"), Path.Combine(root, "la"));
            paths.EnsureCreated();
            var store = new OptimizationHistoryStore(paths);
            var record = new OptimizationHistoryRecord(
                "007FirstLight",
                @"C:\Games\007FirstLight.exe",
                "op.test",
                "اختبار",
                OptimizationHistoryOutcome.Kept,
                DateTimeOffset.UtcNow,
                3.2,
                -4.1,
                -2,
                "تحسن",
                ["سبب"]);

            await store.AppendAsync(record, CancellationToken.None);
            var loaded = await store.GetForGameAsync(Game(), CancellationToken.None);

            var single = Assert.Single(loaded);
            Assert.Equal(OptimizationHistoryOutcome.Kept, single.Outcome);
            Assert.Equal("op.test", single.OperationId);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public async Task Planner_SkipsOperationWithRecentBadOutcome()
    {
        var root = Path.Combine(Path.GetTempPath(), "D7-Learning-Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(Path.Combine(root, "pd"), Path.Combine(root, "la"));
            paths.EnsureCreated();
            var store = new OptimizationHistoryStore(paths);
            await store.AppendAsync(
                new OptimizationHistoryRecord(
                    "007FirstLight",
                    @"C:\Games\007FirstLight.exe",
                    "bad",
                    "تجربة سابقة",
                    OptimizationHistoryOutcome.RolledBack,
                    DateTimeOffset.UtcNow,
                    -3,
                    8,
                    5,
                    "تم التراجع",
                    ["تراجع الأداء"]),
                CancellationToken.None);

            var bad = new FakeOperation("bad", "تجربة سابقة", true);
            var next = new FakeOperation("next", "تجربة أخرى", true);
            var planner = new OptimizationPlanner([bad, next], store);

            var decision = await planner.SelectNextAsync(Game(), CancellationToken.None);

            Assert.Same(next, decision.Operation);
            Assert.Equal(0, bad.PreflightCalls);
            Assert.Equal(1, next.PreflightCalls);
            Assert.Contains(decision.Checks, x => x.OperationId == "bad" && !x.Applicable && x.LearningNoteAr is not null);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void LearningService_MapsConfirmedKeepToKeptOutcome()
    {
        var comparison = new BenchmarkComparison(
            BenchmarkVerdict.Keep,
            "تحسن",
            4,
            -3,
            -1,
            ["تحسن متكرر"]);
        var result = new OptimizationExperimentResult(
            true,
            Game(),
            Capture(),
            Capture(),
            comparison,
            "TX-1",
            false,
            "تم الاعتماد",
            Confirmation: Capture(),
            ConfirmationComparison: comparison);

        var outcome = OptimizationLearningService.ResolveOutcome(result, comparison);

        Assert.Equal(OptimizationHistoryOutcome.Kept, outcome);
    }

    private static ActiveGame Game() =>
        new(777, "007FirstLight", @"C:\Games\007FirstLight.exe", 100, "عالٍ", ["اختبار"]);

    private static BenchmarkCaptureResult Capture() =>
        new(true, 777, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(20), "capture.csv", null, "ok");

    private sealed class FakeOperation(string id, string name, bool applicable) : IReversibleOptimizationOperation
    {
        public string Id => id;
        public string NameAr => name;
        public string Risk => "LOW";
        public int PreflightCalls { get; private set; }

        public Task<OperationPreflightResult> PreflightAsync(OperationContext context, CancellationToken cancellationToken)
        {
            PreflightCalls++;
            return Task.FromResult(new OperationPreflightResult(applicable, applicable ? "ينطبق" : "لا ينطبق"));
        }

        public Task<CapturedOperationState> CaptureStateAsync(OperationContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OperationApplyResult> ApplyAsync(OperationContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> RollbackAsync(OperationContext context, CapturedOperationState capturedState, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
