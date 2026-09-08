using D7.Benchmark.Models;

namespace D7.Benchmark.Comparison;

public static class BenchmarkComparator
{
    public static BenchmarkComparison Compare(
        FrameAnalysis baseline,
        FrameAnalysis candidate,
        int baselineStabilityIssues = 0,
        int candidateStabilityIssues = 0)
    {
        var reasons = new List<string>();
        if (!baseline.Valid || !candidate.Valid || baseline.FrameCount < 120 || candidate.FrameCount < 120)
        {
            reasons.Add("أحد القياسين لا يحتوي بيانات كافية للمقارنة.");
            return New(BenchmarkVerdict.Inconclusive, "النتيجة غير حاسمة وتحتاج قياسًا صالحًا إضافيًا.", 0, 0, 0, reasons);
        }

        if (candidateStabilityIssues > baselineStabilityIssues)
        {
            reasons.Add("ظهرت مشاكل ثبات إضافية بعد التعديل.");
            return New(
                BenchmarkVerdict.Rollback,
                "التعديل يضر الثبات، لذلك يجب التراجع عنه.",
                Percent(candidate.OnePercentLowAverageFps, baseline.OnePercentLowAverageFps),
                Percent(candidate.P99FrameTimeMs, baseline.P99FrameTimeMs),
                candidate.StutterCount - baseline.StutterCount,
                reasons);
        }

        var low1Delta = Percent(candidate.OnePercentLowAverageFps, baseline.OnePercentLowAverageFps);
        var p99Delta = Percent(candidate.P99FrameTimeMs, baseline.P99FrameTimeMs);
        var stutterDelta = candidate.StutterCount - baseline.StutterCount;

        if (low1Delta <= -2.0)
            reasons.Add($"أقل 1% تراجع بنسبة {Math.Abs(low1Delta):0.0}%.");
        if (p99Delta >= 5.0)
            reasons.Add($"P99 أصبح أسوأ بنسبة {p99Delta:0.0}%.");
        if (stutterDelta > Math.Max(2, (int)Math.Ceiling(baseline.StutterCount * 0.20)))
            reasons.Add("عدد التقطعات ارتفع بشكل واضح.");

        if (reasons.Count > 0)
            return New(BenchmarkVerdict.Rollback, "القياس يشير إلى تراجع، لذلك يجب استعادة الإعداد السابق.", low1Delta, p99Delta, stutterDelta, reasons);

        var meaningfulLowGain = low1Delta >= 2.0;
        var p99NotWorse = p99Delta <= 2.0;
        var stuttersNotWorse = stutterDelta <= Math.Max(1, (int)Math.Ceiling(baseline.StutterCount * 0.10));

        if (meaningfulLowGain && p99NotWorse && stuttersNotWorse)
        {
            reasons.Add($"أقل 1% تحسن بنسبة {low1Delta:0.0}% مع ثبات P99 والتقطعات.");
            return New(BenchmarkVerdict.Keep, "التعديل حسن الأداء والثبات بما يكفي للاحتفاظ به.", low1Delta, p99Delta, stutterDelta, reasons);
        }

        reasons.Add("الفروقات ضمن منطقة عدم اليقين ولا تبرر الاحتفاظ أو التراجع تلقائيًا.");
        return New(BenchmarkVerdict.Inconclusive, "النتيجة غير حاسمة؛ يلزم تكرار القياس قبل اتخاذ قرار.", low1Delta, p99Delta, stutterDelta, reasons);
    }

    private static double Percent(double current, double baseline) =>
        baseline == 0 ? 0 : ((current - baseline) / baseline) * 100.0;

    private static BenchmarkComparison New(
        BenchmarkVerdict verdict,
        string summary,
        double low,
        double p99,
        int stutter,
        IReadOnlyList<string> reasons) =>
        new(verdict, summary, Math.Round(low, 2), Math.Round(p99, 2), stutter, reasons);
}
