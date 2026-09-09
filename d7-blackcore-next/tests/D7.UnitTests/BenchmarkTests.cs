using D7.Benchmark.Comparison;
using D7.Benchmark.Confidence;
using D7.Benchmark.Models;
using D7.Benchmark.Statistics;
using Xunit;

namespace D7.UnitTests;

public sealed class BenchmarkTests
{
    [Fact]
    public void AverageFps_UsesTotalFrameTime()
    {
        var frames = Enumerable.Repeat(10d, 990).Concat(Enumerable.Repeat(20d, 10)).ToArray();
        var result = FrameStatistics.Analyze(frames, "MsBetweenPresents");

        Assert.True(result.Valid);
        Assert.Equal(1000, result.FrameCount);
        Assert.InRange(result.AverageFps, 98.9, 99.1);
        Assert.InRange(result.OnePercentLowAverageFps, 49.9, 50.1);
    }

    [Fact]
    public void QuantileR7_Interpolates()
    {
        var values = new[] { 1d, 2d, 3d, 4d, 5d };
        Assert.Equal(3d, FrameStatistics.QuantileR7(values, 0.5));
        Assert.Equal(4.6d, FrameStatistics.QuantileR7(values, 0.9), 10);
    }

    [Fact]
    public void Comparator_RollsBack_WhenStabilityRegresses()
    {
        var baseline = Sample(low1: 60, p99: 18, stutters: 3);
        var candidate = Sample(low1: 65, p99: 17, stutters: 2);

        var comparison = BenchmarkComparator.Compare(baseline, candidate, baselineStabilityIssues: 0, candidateStabilityIssues: 1);

        Assert.Equal(BenchmarkVerdict.Rollback, comparison.Verdict);
    }

    [Fact]
    public void Comparator_Keeps_MeaningfulLowGainWithoutP99Regression()
    {
        var baseline = Sample(low1: 60, p99: 20, stutters: 5);
        var candidate = Sample(low1: 64, p99: 19, stutters: 4);

        var comparison = BenchmarkComparator.Compare(baseline, candidate);

        Assert.Equal(BenchmarkVerdict.Keep, comparison.Verdict);
    }

    [Fact]
    public void Confidence_IsHigh_ForFullDurationLargeSample()
    {
        var analysis = new FrameAnalysis(
            true,
            1200,
            60,
            50,
            42,
            16.6667,
            16.5,
            18,
            20,
            25,
            2,
            0.2,
            "MsBetweenPresents",
            FrameStatistics.MethodVersion,
            Array.Empty<string>());

        var confidence = BenchmarkConfidenceEvaluator.Evaluate(analysis, TimeSpan.FromSeconds(20));

        Assert.Equal(BenchmarkConfidenceLevel.High, confidence.Level);
        Assert.True(confidence.AutomaticDecisionAllowed);
        Assert.InRange(confidence.DurationCoverageRatio, 0.99, 1.01);
    }

    [Fact]
    public void Confidence_RejectsWeakCoverage_ForAutomaticDecision()
    {
        var analysis = new FrameAnalysis(
            true,
            80,
            80,
            60,
            50,
            12.5,
            12,
            15,
            20,
            30,
            1,
            0.1,
            "MsBetweenPresents",
            FrameStatistics.MethodVersion,
            Array.Empty<string>());

        var confidence = BenchmarkConfidenceEvaluator.Evaluate(analysis, TimeSpan.FromSeconds(20));

        Assert.False(confidence.AutomaticDecisionAllowed);
        Assert.True(confidence.Level is BenchmarkConfidenceLevel.Low or BenchmarkConfidenceLevel.Rejected);
        Assert.True(confidence.DurationCoverageRatio < 0.35);
    }

    private static FrameAnalysis Sample(double low1, double p99, int stutters) =>
        new(true, 2000, 100, low1, 50, 10, 10, 15, p99, 30, stutters, 0.1, "MsBetweenPresents", FrameStatistics.MethodVersion, Array.Empty<string>());
}
