using D7.Benchmark.Models;

namespace D7.Benchmark.Confidence;

public enum BenchmarkConfidenceLevel
{
    Rejected,
    Low,
    Medium,
    High
}

public sealed record BenchmarkConfidence(
    int Score,
    BenchmarkConfidenceLevel Level,
    double DurationCoverageRatio,
    int FrameCount,
    IReadOnlyList<string> Reasons)
{
    public bool AutomaticDecisionAllowed => Level is BenchmarkConfidenceLevel.Medium or BenchmarkConfidenceLevel.High;

    public string LabelAr => Level switch
    {
        BenchmarkConfidenceLevel.High => "عالية",
        BenchmarkConfidenceLevel.Medium => "متوسطة",
        BenchmarkConfidenceLevel.Low => "منخفضة",
        _ => "مرفوضة"
    };
}

public static class BenchmarkConfidenceEvaluator
{
    public const string MethodVersion = "D7.Confidence.1";

    public static BenchmarkConfidence Evaluate(FrameAnalysis? analysis, TimeSpan requestedDuration)
    {
        var reasons = new List<string>();
        if (analysis is null || !analysis.Valid)
        {
            reasons.Add("التحليل غير صالح، لذلك لا يمكن استخدام القياس في قرار تلقائي.");
            return new BenchmarkConfidence(0, BenchmarkConfidenceLevel.Rejected, 0, analysis?.FrameCount ?? 0, reasons);
        }

        if (requestedDuration <= TimeSpan.Zero)
        {
            reasons.Add("مدة القياس المطلوبة غير صالحة.");
            return new BenchmarkConfidence(0, BenchmarkConfidenceLevel.Rejected, 0, analysis.FrameCount, reasons);
        }

        var score = 35; // A valid parsed PresentMon capture is the first gate.
        var capturedMs = Math.Max(0, analysis.AverageFrameTimeMs * analysis.FrameCount);
        var requestedMs = requestedDuration.TotalMilliseconds;
        var coverage = requestedMs <= 0 ? 0 : capturedMs / requestedMs;

        if (coverage is >= 0.80 and <= 1.25)
        {
            score += 30;
            reasons.Add("مدة الإطارات الملتقطة قريبة من مدة القياس المطلوبة.");
        }
        else if (coverage is >= 0.60 and <= 1.50)
        {
            score += 18;
            reasons.Add("تغطية مدة القياس مقبولة لكنها ليست مثالية.");
        }
        else if (coverage >= 0.35)
        {
            score += 8;
            reasons.Add("تغطية مدة القياس ضعيفة وقد تحتوي فترات تحميل أو توقف أو خروج من اللعبة.");
        }
        else
        {
            reasons.Add("جزء صغير فقط من مدة الاختبار يحتوي إطارات قابلة للتحليل.");
        }

        if (analysis.FrameCount >= 600)
        {
            score += 20;
            reasons.Add("حجم العينة كبير بما يكفي لقياس ثبات الإطارات.");
        }
        else if (analysis.FrameCount >= 240)
        {
            score += 15;
            reasons.Add("حجم العينة مقبول.");
        }
        else if (analysis.FrameCount >= 120)
        {
            score += 8;
            reasons.Add("حجم العينة محدود؛ النتيجة تحتاج حذرًا أكبر.");
        }
        else
        {
            reasons.Add("عدد الإطارات قليل جدًا لاتخاذ قرار تلقائي قوي.");
        }

        if (analysis.Warnings.Count == 0)
        {
            score += 10;
        }
        else
        {
            reasons.Add($"التحليل يحتوي {analysis.Warnings.Count} تحذير/تحذيرات.");
        }

        if (string.Equals(analysis.FrameTimeColumn, "MsBetweenPresents", StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
        }
        else if (!string.IsNullOrWhiteSpace(analysis.FrameTimeColumn))
        {
            score += 2;
            reasons.Add($"تم استخدام عمود بديل لزمن الإطار: {analysis.FrameTimeColumn}.");
        }

        score = Math.Clamp(score, 0, 100);
        var level = score switch
        {
            >= 85 => BenchmarkConfidenceLevel.High,
            >= 65 => BenchmarkConfidenceLevel.Medium,
            >= 45 => BenchmarkConfidenceLevel.Low,
            _ => BenchmarkConfidenceLevel.Rejected
        };

        if (level is BenchmarkConfidenceLevel.Low or BenchmarkConfidenceLevel.Rejected)
            reasons.Add("D7 لن يستخدم هذا القياس للاحتفاظ بتعديل تلقائي.");

        return new BenchmarkConfidence(score, level, Math.Round(coverage, 3), analysis.FrameCount, reasons);
    }
}
