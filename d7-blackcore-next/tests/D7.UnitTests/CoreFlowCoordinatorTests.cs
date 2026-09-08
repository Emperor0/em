using System.Text.Json;
using D7.Benchmark.Capture;
using D7.Benchmark.Models;
using D7.Core.Foundation;
using D7.Core.Logging;
using D7.Games.Detection;
using D7.Optimization.Contracts;
using D7.Orchestration;
using D7.Rollback.Journal;
using D7.Rollback.Models;
using Xunit;

namespace D7.UnitTests;

public sealed class CoreFlowCoordinatorTests
{
    [Fact]
    public async Task NoGame_MeansNoModification()
    {
        using var fixture = new Fixture(null, []);
        var result = await fixture.Coordinator.RunExperimentAsync(new FakeOperation(), TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.TransactionId);
        Assert.Equal(0, fixture.FrameCapture.Calls);
    }

    [Fact]
    public async Task StrongImprovement_RequiresConfirmationBeforeCommit()
    {
        var baseline = Capture(60, 20, 5);
        var candidate = Capture(65, 19, 4);
        var confirmation = Capture(64.5, 19.2, 4);
        using var fixture = new Fixture(Game(), [baseline, candidate, confirmation]);
        var operation = new FakeOperation();

        var result = await fixture.Coordinator.RunExperimentAsync(operation, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Comparison);
        Assert.Equal(BenchmarkVerdict.Keep, result.Comparison!.Verdict);
        Assert.NotNull(result.Confirmation);
        Assert.NotNull(result.ConfirmationComparison);
        Assert.False(result.RolledBack);
        Assert.Equal(3, fixture.FrameCapture.Calls);
        Assert.Equal(1, operation.ApplyCalls);
        Assert.Equal(0, operation.RollbackCalls);

        var incomplete = await fixture.Journal.FindIncompleteAsync(CancellationToken.None);
        Assert.Empty(incomplete);
    }

    [Fact]
    public async Task FirstPassGainThatDoesNotRepeat_IsRolledBack()
    {
        var baseline = Capture(60, 20, 5);
        var candidate = Capture(65, 19, 4);
        var confirmation = Capture(60.5, 20.1, 5);
        using var fixture = new Fixture(Game(), [baseline, candidate, confirmation]);
        var operation = new FakeOperation();

        var result = await fixture.Coordinator.RunExperimentAsync(operation, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.ConfirmationComparison);
        Assert.Equal(BenchmarkVerdict.Inconclusive, result.ConfirmationComparison!.Verdict);
        Assert.True(result.RolledBack);
        Assert.Equal(1, operation.RollbackCalls);
    }

    [Fact]
    public async Task InconclusiveResult_ConservativelyRollsBack()
    {
        var baseline = Capture(60, 20, 5);
        var candidate = Capture(60.5, 20.1, 5);
        using var fixture = new Fixture(Game(), [baseline, candidate]);
        var operation = new FakeOperation();

        var result = await fixture.Coordinator.RunExperimentAsync(operation, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Comparison);
        Assert.Equal(BenchmarkVerdict.Inconclusive, result.Comparison!.Verdict);
        Assert.True(result.RolledBack);
        Assert.Equal(1, operation.RollbackCalls);
    }

    [Fact]
    public async Task InvalidCandidate_RollsBackAndNeverKeepsChange()
    {
        var baseline = Capture(60, 20, 5);
        var invalidCandidate = new BenchmarkCaptureResult(false, 777, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5), null, null, "فشل القياس");
        using var fixture = new Fixture(Game(), [baseline, invalidCandidate]);
        var operation = new FakeOperation();

        var result = await fixture.Coordinator.RunExperimentAsync(operation, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.RolledBack);
        Assert.Equal(1, operation.RollbackCalls);
    }

    private static ActiveGame Game() => new(777, "007FirstLight", @"S:\GM\007 First Light\007FirstLight.exe", 100, "عالٍ", ["اختبار"]);

    private static BenchmarkCaptureResult Capture(double low1, double p99, int stutters)
    {
        var analysis = new FrameAnalysis(
            true, 3000, 100, low1, 50, 10, 10, 15, p99, 30, stutters, 0.1,
            "MsBetweenPresents", "D7.FrameStats.1", Array.Empty<string>());
        return new BenchmarkCaptureResult(true, 777, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5), "test.csv", analysis, "ok");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root;
        public FakeFrameCapture FrameCapture { get; }
        public TransactionJournal Journal { get; }
        public CoreFlowCoordinator Coordinator { get; }

        public Fixture(ActiveGame? game, IReadOnlyList<BenchmarkCaptureResult> captures)
        {
            _root = Path.Combine(Path.GetTempPath(), "D7-CoreFlow-Tests", Guid.NewGuid().ToString("N"));
            var paths = new AppPaths(Path.Combine(_root, "pd"), Path.Combine(_root, "la"));
            paths.EnsureCreated();
            var logger = new JsonLineLogger(paths);
            Journal = new TransactionJournal(paths);
            FrameCapture = new FakeFrameCapture(captures);
            Coordinator = new CoreFlowCoordinator(new FakeGameDetector(game), FrameCapture, Journal, logger);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }
    }

    private sealed class FakeGameDetector(ActiveGame? game) : IActiveGameDetector
    {
        public Task<ActiveGame?> DetectActiveGameAsync(CancellationToken cancellationToken) => Task.FromResult(game);
    }

    public sealed class FakeFrameCapture(IReadOnlyList<BenchmarkCaptureResult> results) : IFrameCaptureService
    {
        private int _index;
        public int Calls { get; private set; }

        public Task<BenchmarkCaptureResult> CaptureAsync(int processId, TimeSpan duration, CancellationToken cancellationToken)
        {
            Calls++;
            if (_index >= results.Count) throw new InvalidOperationException("No fake capture result configured.");
            return Task.FromResult(results[_index++]);
        }
    }

    private sealed class FakeOperation : IReversibleOptimizationOperation
    {
        private sealed record State(string Value);

        public string Id => "test.safe-operation";
        public string NameAr => "تعديل اختباري آمن";
        public string Risk => "LOW";
        public int ApplyCalls { get; private set; }
        public int RollbackCalls { get; private set; }

        public Task<CapturedOperationState> CaptureStateAsync(OperationContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new CapturedOperationState("test", context.ProcessId.ToString(), JsonSerializer.Serialize(new State("before"))));

        public Task<OperationApplyResult> ApplyAsync(OperationContext context, CancellationToken cancellationToken)
        {
            ApplyCalls++;
            return Task.FromResult(new OperationApplyResult(true, true, JsonSerializer.Serialize(new State("after")), "applied"));
        }

        public Task<bool> RollbackAsync(OperationContext context, CapturedOperationState capturedState, CancellationToken cancellationToken)
        {
            RollbackCalls++;
            return Task.FromResult(true);
        }
    }
}
