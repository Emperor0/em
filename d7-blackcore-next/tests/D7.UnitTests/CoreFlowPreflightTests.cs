using D7.Benchmark.Capture;
using D7.Benchmark.Models;
using D7.Core.Foundation;
using D7.Core.Logging;
using D7.Games.Detection;
using D7.Optimization.Contracts;
using D7.Orchestration;
using D7.Rollback.Journal;
using Xunit;

namespace D7.UnitTests;

public sealed class CoreFlowPreflightTests
{
    [Fact]
    public async Task NotApplicableOperation_DoesNotBenchmarkOrApply()
    {
        var root = Path.Combine(Path.GetTempPath(), "D7-Preflight-Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(Path.Combine(root, "pd"), Path.Combine(root, "la"));
            paths.EnsureCreated();
            var capture = new CountingCapture();
            var operation = new NotApplicableOperation();
            var coordinator = new CoreFlowCoordinator(
                new FixedGameDetector(new ActiveGame(123, "007FirstLight", @"C:\Games\007FirstLight.exe", 100, "عالٍ", ["اختبار"])),
                capture,
                new TransactionJournal(paths),
                new JsonLineLogger(paths));

            var result = await coordinator.RunExperimentAsync(operation, TimeSpan.FromSeconds(20), CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal(0, capture.Calls);
            Assert.Equal(1, operation.PreflightCalls);
            Assert.Equal(0, operation.CaptureCalls);
            Assert.Equal(0, operation.ApplyCalls);
            Assert.Null(result.TransactionId);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    private sealed class FixedGameDetector(ActiveGame game) : IActiveGameDetector
    {
        public Task<ActiveGame?> DetectActiveGameAsync(CancellationToken cancellationToken) => Task.FromResult<ActiveGame?>(game);
    }

    private sealed class CountingCapture : IFrameCaptureService
    {
        public int Calls { get; private set; }

        public Task<BenchmarkCaptureResult> CaptureAsync(int processId, TimeSpan duration, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("Capture must not run when preflight says no-op.");
        }
    }

    private sealed class NotApplicableOperation : IReversibleOptimizationOperation
    {
        public string Id => "test.noop";
        public string NameAr => "اختبار غير منطبق";
        public string Risk => "LOW";
        public int PreflightCalls { get; private set; }
        public int CaptureCalls { get; private set; }
        public int ApplyCalls { get; private set; }

        public Task<OperationPreflightResult> PreflightAsync(OperationContext context, CancellationToken cancellationToken)
        {
            PreflightCalls++;
            return Task.FromResult(new OperationPreflightResult(false, "الإعداد مضبوط مسبقًا."));
        }

        public Task<CapturedOperationState> CaptureStateAsync(OperationContext context, CancellationToken cancellationToken)
        {
            CaptureCalls++;
            throw new InvalidOperationException("CaptureState must not run.");
        }

        public Task<OperationApplyResult> ApplyAsync(OperationContext context, CancellationToken cancellationToken)
        {
            ApplyCalls++;
            throw new InvalidOperationException("Apply must not run.");
        }

        public Task<bool> RollbackAsync(OperationContext context, CapturedOperationState capturedState, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }
}
