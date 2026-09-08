using D7.Benchmark.Models;

namespace D7.Benchmark.Statistics;

public static class FrameStatistics
{
    public const string MethodVersion = "D7.FrameStats.1";

    public static FrameAnalysis Analyze(IReadOnlyList<double> frameTimesMs, string sourceColumn)
    {
        var warnings = new List<string>();
        var chronological = frameTimesMs.Where(x => double.IsFinite(x) && x > 0 && x < 1000).ToArray();
        if (chronological.Length < 60)
        {
            warnings.Add("عدد الإطارات غير كافٍ لإصدار حكم موثوق.");
            return new FrameAnalysis(false, chronological.Length, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, sourceColumn, MethodVersion, warnings);
        }

        var sorted = chronological.ToArray();
        Array.Sort(sorted);
        var total = chronological.Sum();
        var averageFrame = total / chronological.Length;
        var averageFps = chronological.Length * 1000.0 / total;
        var median = QuantileR7(sorted, 0.50);
        var p95 = QuantileR7(sorted, 0.95);
        var p99 = QuantileR7(sorted, 0.99);
        var p999 = QuantileR7(sorted, 0.999);
        var low1Frame = HighTailAverage(sorted, 0.99);
        var low01Frame = HighTailAverage(sorted, 0.999);
        var low1Fps = 1000.0 / low1Frame;
        var low01Fps = 1000.0 / low01Frame;

        var stutter = AnalyzeStutters(chronological, 2.5);
        return new FrameAnalysis(
            true,
            chronological.Length,
            Round(averageFps, 2),
            Round(low1Fps, 2),
            Round(low01Fps, 2),
            Round(averageFrame, 3),
            Round(median, 3),
            Round(p95, 3),
            Round(p99, 3),
            Round(p999, 3),
            stutter.Count,
            Round(stutter.TimePercent, 3),
            sourceColumn,
            MethodVersion,
            warnings);
    }

    public static double QuantileR7(IReadOnlyList<double> ascending, double probability)
    {
        if (ascending.Count == 0) return double.NaN;
        if (probability <= 0) return ascending[0];
        if (probability >= 1) return ascending[^1];
        var h = (ascending.Count - 1) * probability;
        var lower = (int)Math.Floor(h);
        var upper = (int)Math.Ceiling(h);
        if (lower == upper) return ascending[lower];
        var fraction = h - lower;
        return ascending[lower] + ((ascending[upper] - ascending[lower]) * fraction);
    }

    private static double HighTailAverage(IReadOnlyList<double> ascending, double quantileProbability)
    {
        var threshold = QuantileR7(ascending, quantileProbability);
        var sum = 0d;
        var count = 0;
        for (var i = 0; i < ascending.Count; i++)
        {
            if (ascending[i] < threshold) continue;
            sum += ascending[i];
            count++;
        }
        return count == 0 ? threshold : sum / count;
    }

    private static (int Count, double TimePercent) AnalyzeStutters(IReadOnlyList<double> chronological, double factor)
    {
        var average = chronological.Average();
        var window = Math.Max(5, (int)Math.Round(Math.Sqrt(average) * 10));
        var rolling = new Queue<double>(window);
        var rollingSum = 0d;
        var stutterCount = 0;
        var stutterTime = 0d;
        var totalTime = chronological.Sum();

        foreach (var frame in chronological)
        {
            var localAverage = rolling.Count > 0 ? rollingSum / rolling.Count : average;
            if (rolling.Count >= Math.Min(5, window) && frame > factor * localAverage)
            {
                stutterCount++;
                stutterTime += frame;
            }
            rolling.Enqueue(frame);
            rollingSum += frame;
            if (rolling.Count > window) rollingSum -= rolling.Dequeue();
        }

        return (stutterCount, totalTime > 0 ? 100 * stutterTime / totalTime : 0);
    }

    private static double Round(double value, int digits) => Math.Round(value, digits, MidpointRounding.AwayFromZero);
}
