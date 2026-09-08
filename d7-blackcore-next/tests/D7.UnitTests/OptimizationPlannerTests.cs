using D7.Games.Detection;
using D7.Optimization.Contracts;
using D7.Orchestration.Planning;
using Xunit;

namespace D7.UnitTests;

public sealed class OptimizationPlannerTests
{
    [Fact]
    public async Task Planner_SkipsNotApplicableAndSelectsNextLowRiskOperation()
    {
        var first = new FakeOperation("first", "الأولى", "LOW", applicable: false);
        var second = new FakeOperation("second", "الثانية", "LOW", applicable: true);
        var planner = new OptimizationPlanner([first, second]);

        var decision = await planner.SelectNextAsync(Game(), CancellationToken.None);

        Assert.True(decision.HasOperation);
        Assert.Same(second, decision.Operation);
        Assert.Equal(2, decision.Checks.Count);
        Assert.False(decision.Checks[0].Applicable);
        Assert.True(decision.Checks[1].Applicable);
        Assert.Equal(1, first.PreflightCalls);
        Assert.Equal(1, second.PreflightCalls);
    }

    [Fact]
    public async Task Planner_NeverAutoSelectsNonLowRiskOperation()
    {
        var risky = new FakeOperation("risky", "تجربة متقدمة", "MEDIUM", applicable: true);
        var safe = new FakeOperation("safe", "تجربة آمنة", "LOW", applicable: true);
        var planner = new OptimizationPlanner([risky, safe]);

        var decision = await planner.SelectNextAsync(Game(), CancellationToken.None);

        Assert.Same(safe, decision.Operation);
        Assert.Equal(0, risky.PreflightCalls);
        Assert.Equal(1, safe.PreflightCalls);
        Assert.Contains("المخاطر", decision.Checks[0].MessageAr);
    }

    [Fact]
    public async Task Planner_ReturnsNoOperationWhenEverythingIsNoOp()
    {
        var planner = new OptimizationPlanner([
            new FakeOperation("one", "واحد", "LOW", applicable: false),
            new FakeOperation("two", "اثنان", "LOW", applicable: false)
        ]);

        var decision = await planner.SelectNextAsync(Game(), CancellationToken.None);

        Assert.False(decision.HasOperation);
        Assert.Null(decision.Operation);
        Assert.Equal(2, decision.Checks.Count);
    }

    private static ActiveGame Game() =>
        new(777, "007FirstLight", @"C:\Games\007FirstLight.exe", 100, "عالٍ", ["اختبار"]);

    private sealed class FakeOperation(
        string id,
        string nameAr,
        string risk,
        bool applicable) : IReversibleOptimizationOperation
    {
        public string Id => id;
        public string NameAr => nameAr;
        public string Risk => risk;
        public int PreflightCalls { get; private set; }

        public Task<OperationPreflightResult> PreflightAsync(OperationContext context, CancellationToken cancellationToken)
        {
            PreflightCalls++;
            return Task.FromResult(new OperationPreflightResult(applicable, applicable ? "ينطبق" : "لا ينطبق"));
        }

        public Task<CapturedOperationState> CaptureStateAsync(OperationContext context, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationApplyResult> ApplyAsync(OperationContext context, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> RollbackAsync(OperationContext context, CapturedOperationState capturedState, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
