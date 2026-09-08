using D7.Core.Models;
using D7.Hardware.Models;

namespace D7.Hardware.Capabilities;

public sealed class HardwareCapabilityEvaluator
{
    public IReadOnlyList<Capability> Evaluate(HardwareSnapshot snapshot)
    {
        var capabilities = new List<Capability>
        {
            new(
                "windows.game_mode",
                "وضع الألعاب في ويندوز",
                CapabilityState.Supported,
                "يمكن لـ D7 قراءة حالة وضع الألعاب وتطبيق تغيير قابل للتراجع بعد اكتمال محرك العمليات الآمنة.",
                $"Current state: {snapshot.Windows.GameMode}"),
            new(
                "telemetry.cpu_memory",
                "قياس المعالج والذاكرة",
                CapabilityState.Supported,
                "متاح عبر واجهات Windows خفيفة بدون فحص WMI متكرر."),
            new(
                "network.analysis",
                "تحليل الشبكة",
                CapabilityState.ReadOnly,
                "المرحلة الحالية تقرأ حالة المحولات فقط. تعديلات RSS/MSI/offload لا تُطبق قبل القياس والتراجع."),
            new(
                "ram.analysis",
                "تحليل الذاكرة",
                CapabilityState.ReadOnly,
                "D7 لا يكتب توقيتات الرام من Windows؛ التحسين المتقدم يبقى Advisor إلى أن توجد طريقة آمنة مثبتة."),
            new(
                "bios.update",
                "تحديث BIOS",
                CapabilityState.ReadOnly,
                "يمكن تحليل الإصدار لاحقًا، لكن تحديث BIOS لن يكون تلقائيًا."),
            new(
                "cpu.low_level_tuning",
                "الضبط منخفض المستوى للمعالج",
                CapabilityState.Experimental,
                "مغلق افتراضيًا حتى اكتمال القياس واختبارات الثبات والتراجع والتحقق من دعم المعالج."),
            new(
                "gpu.overclock",
                "الضبط التلقائي لكرت الشاشة",
                CapabilityState.Experimental,
                "مغلق افتراضيًا حتى اكتمال Frame Lab وStability Lab ومسار التراجع."),
            new(
                "anti_cheat_manipulation",
                "التعامل مع ذاكرة اللعبة أو Anti-Cheat",
                CapabilityState.Unavailable,
                "D7 لا يحقن داخل الألعاب ولا يقرأ أو يكتب ذاكرة اللعبة ولا يحاول تجاوز Anti-Cheat.")
        };

        var nvidia = snapshot.DisplayAdapters.Any(x =>
            !x.MirroringDriver && x.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
        capabilities.Add(new Capability(
            "telemetry.nvidia",
            "قياس NVIDIA",
            nvidia ? CapabilityState.ReadOnly : CapabilityState.Unavailable,
            nvidia
                ? "تم اكتشاف NVIDIA؛ القياس اللحظي يستخدم واجهة قراءة فقط عند توفرها."
                : "لم يتم اكتشاف محول NVIDIA صالح في الفحص الحالي."));

        return capabilities;
    }
}
