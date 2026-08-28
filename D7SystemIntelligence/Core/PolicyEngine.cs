namespace D7SystemIntelligence.Core;

public sealed class PolicyEngine
{
    public IReadOnlyList<PolicyDecision> Evaluate(HardwareSnapshot s, RuntimeContext context, D7Profile profile)
    {
        var decisions = new List<PolicyDecision>();

        if (s.CpuTemp >= 90)
            decisions.Add(new("Critical", "Thermal", "حرارة المعالج مرتفعة جدًا", $"حرارة المعالج الآن {s.CpuTemp:0}°C. خفّف الحمل وافحص التبريد فورًا."));
        else if (s.CpuTemp >= 82)
            decisions.Add(new("Warning", "Thermal", "حرارة المعالج مرتفعة", $"حرارة المعالج {s.CpuTemp:0}°C وتحتاج مراقبة أثناء الحمل."));

        if (s.GpuTemp >= 88)
            decisions.Add(new("Critical", "Thermal", "حرارة كرت الشاشة مرتفعة جدًا", $"حرارة كرت الشاشة الآن {s.GpuTemp:0}°C."));
        else if (s.GpuTemp >= 82)
            decisions.Add(new("Warning", "Thermal", "حرارة كرت الشاشة مرتفعة", $"حرارة كرت الشاشة {s.GpuTemp:0}°C وتحتاج مراقبة."));

        if (s.RamLoad >= 92)
            decisions.Add(new("Critical", "Memory", "ضغط الذاكرة مرتفع جدًا", $"استخدام RAM وصل {s.RamLoad:0}%. هذا قد يسبب paging وتقطيع."));
        else if (s.RamLoad >= 84)
            decisions.Add(new("Warning", "Memory", "ضغط RAM مرتفع", $"استخدام RAM الآن {s.RamLoad:0}%. D7 سيراقب أي تأثير على frametime."));

        if (context.Mode is D7RuntimeMode.Gaming or D7RuntimeMode.StreamGaming)
        {
            if (s.CpuLoad >= 94 && s.GpuLoad < 90)
                decisions.Add(new("Warning", "Performance", "اختناق محتمل من المعالج", $"CPU {s.CpuLoad:0}% مقابل GPU {s.GpuLoad:0}%. الأولوية ستكون لحماية 1% lows وتقليل حمل الخلفية."));

            if (s.GpuLoad >= 97 && s.CpuLoad < 90)
                decisions.Add(new("Info", "Performance", "اللعبة محدودة بكرت الشاشة", $"GPU يعمل عند {s.GpuLoad:0}%. هذا طبيعي إذا كان الهدف أعلى استخدام للكرت."));
        }

        if (context.Mode == D7RuntimeMode.StreamGaming && s.CpuLoad >= 88)
            decisions.Add(new("Warning", "Streaming", "هامش البث على المعالج منخفض", $"CPU {s.CpuLoad:0}% أثناء اللعب والبث. D7 سيعتبر ثبات البث و1% lows أولوية."));

        if (s.Fans.Count > 0 && s.Fans.All(f => !f.Controllable))
            decisions.Add(new("Info", "Fans", "المراوح للقراءة فقط حاليًا", "D7 يقرأ RPM لكن اللوحة لم تعرض قناة PWM قابلة للكتابة. لن يفرض تحكمًا غير مدعوم."));

        if (profile == D7Profile.MaxPerformance && context.Mode is D7RuntimeMode.Gaming or D7RuntimeMode.StreamGaming)
            decisions.Add(new("Info", "Profile", "وضع أقصى أداء فعّال", "سياسة D7 الحالية تفضّل الاستجابة و1% lows وتؤجل أي صيانة غير ضرورية أثناء اللعب.", true));

        return decisions;
    }
}
