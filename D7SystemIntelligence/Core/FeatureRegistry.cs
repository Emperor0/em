using System.Diagnostics;

namespace D7SystemIntelligence.Core;

public enum D7FeatureState
{
    Ready,
    Partial,
    ReadOnly,
    Unavailable,
    Planned
}

public sealed record D7FeatureCapability(
    string Id,
    string NameArabic,
    string NameEnglish,
    D7FeatureState State,
    string Detail,
    bool HardwareDependent = false);

public sealed class FeatureRegistry
{
    public IReadOnlyList<D7FeatureCapability> Detect(HardwareSnapshot? snapshot = null)
    {
        var processes = Process.GetProcesses()
            .Select(SafeName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var hasObs = processes.Any(x => x.Equals("obs64", StringComparison.OrdinalIgnoreCase) || x.Equals("obs32", StringComparison.OrdinalIgnoreCase) || x.Equals("obs", StringComparison.OrdinalIgnoreCase));
        var hasTikTok = processes.Any(x => x.Contains("tiktok", StringComparison.OrdinalIgnoreCase));
        var controllableFans = snapshot?.Fans.Count(f => f.Controllable) ?? 0;

        var openRgb = new ManagedOpenRgbService().Detect();
        var presentMon = new ManagedPresentMonService().Detect();
        var ffmpeg = new ManagedFfmpegService().Detect();
        var display = new DisplayControlService();
        var brightness = display.ReadBrightness();

        return new List<D7FeatureCapability>
        {
            new("self-update", "تحديث D7 الذاتي", "D7 Self Update", D7FeatureState.Ready,
                "فحص GitHub Release، مقارنة الإصدار، تنزيل المثبت، SHA-256، تشغيل التثبيت وإعادة فتح D7."),

            new("telemetry", "مراقبة الجهاز", "Hardware Telemetry", D7FeatureState.Ready,
                "قراءة CPU/GPU/RAM/VRAM والحرارة والمراوح من LibreHardwareMonitor."),

            new("missions", "Mission Control", "Mission Control", D7FeatureState.Ready,
                "PRO RANKED / STREAM+RANKED / RECORDING / STORY / SILENT مع Power/Display/Network/Fans/Processes/Replay وRestore."),

            new("auto-scene", "Auto Scene", "Auto Scene", D7FeatureState.Ready,
                "يختار Mission بعد ثبات المشهد ويستعيدها عند إغلاق اللعبة. يبقى OFF افتراضيًا حتى يفعّله المستخدم."),

            new("performance-contract", "Performance Contract", "Performance Contract", D7FeatureState.Ready,
                "يستخدم Telemetry الجلسة نفسها لمراقبة FPS/1%/P99/Temps/RAM/Ping وينفذ Guards آمنة محددة."),

            new("network", "ذكاء الشبكة", "Network Intelligence", D7FeatureState.Ready,
                "Ping/Jitter/Loss/Gateway/DNS/Link + Gaming NIC profile بBackup/Restore + Download Bufferbloat test."),

            new("drivers", "إدارة التعريفات الآمنة", "Safe Driver Manager", D7FeatureState.Ready,
                "Inventory + Windows Update driver scan/install بعد Driver Store backup وRestore Point + إعادة إضافة Backup عند الاستعادة. المقارنة المباشرة مع كل مصنع تبقى منفصلة."),

            new("peripherals", "مختبر الأجهزة الطرفية", "Peripheral / Input Lab", D7FeatureState.Ready,
                "تجميع HID إلى أجهزة فعلية + Raw Input Mouse Polling/Jitter + XInput Controller Drift/Deadzone.", true),

            new("fans", "المراوح الذكية", "Smart Fans",
                controllableFans > 0 ? D7FeatureState.Ready : D7FeatureState.ReadOnly,
                controllableFans > 0
                    ? $"AUTO Curve حقيقية مع hysteresis على {controllableFans} قناة writable واستعادة BIOS/Default."
                    : "RPM/حرارة للقراءة فقط؛ الهاردوير الحالي لم يعرض قناة كتابة آمنة، لذلك D7 لا يرسل PWM عشوائيًا.", true),

            new("shadow-capture", "D7 Shadow Capture", "D7 Shadow Capture",
                hasObs ? D7FeatureState.Ready : D7FeatureState.Unavailable,
                hasObs
                    ? "Replay Buffer فعلي عبر OBS WebSocket، مدة/مجلد/Hotkey/Auto cleanup، بدون تشغيل Recorder ثاني."
                    : "OBS غير شغال حاليًا. D7 يستطيع تشغيل OBS تلقائيًا إذا كان مثبتًا ثم استخدام Replay Buffer الحقيقي.", true),

            new("clip-library", "مكتبة المقاطع", "Clip Library", D7FeatureState.Ready,
                ffmpeg.Available
                    ? "عرض/إعادة تسمية/نقل/حذف + قص سريع بدون re-encode عبر FFmpeg المتحقق."
                    : "إدارة الملفات تعمل، وD7 يجهز FFmpeg المتحقق عند أول عملية قص تحتاجه."),

            new("stream-director", "مدير البث", "Stream Director",
                hasObs || hasTikTok ? D7FeatureState.Ready : D7FeatureState.Unavailable,
                hasObs || hasTikTok
                    ? "يقرأ OBS Stats/WebSocket وحالة Stream/Record/VirtualCam ويملك Process Governor قابلًا للاستعادة."
                    : "لا يوجد OBS/TikTok LIVE Studio شغال حاليًا."),

            new("rgb", "استوديو RGB", "RGB Studio",
                openRgb.Available ? D7FeatureState.Ready : D7FeatureState.Unavailable,
                openRgb.Available
                    ? "OpenRGB backend موجود: List Devices / Static Color / Off / Temperature RGB."
                    : "OpenRGB غير مجهز بعد؛ D7 يستطيع تنزيل الحزمة الرسمية والتحقق من SHA-256 عند التجهيز.", true),

            new("display", "الشاشة والتحكم", "Display Control", D7FeatureState.Ready,
                brightness.Supported
                    ? "Refresh modes/apply/max/restore + DDC/CI brightness متاح على الشاشة الحالية."
                    : "Refresh modes/apply/max/restore تعمل؛ DDC/CI brightness غير متاح/معطل على الشاشة الحالية.", true),

            new("audio", "Audio Studio", "Audio Studio", D7FeatureState.Ready,
                "قراءة endpoints وSample Rate/Channels + Volume/Mute + Default Game/Desktop/Communications + Restore Vault."),

            new("overlay", "D7 HUD", "D7 HUD", D7FeatureState.Ready,
                presentMon.Available
                    ? "HUD click-through: FPS/1%/P99/CPU/GPU/RAM/Ping/Jitter عبر PresentMon."
                    : "HUD جاهز ويجهز PresentMon الرسمي مع SHA-256 عند أول تشغيل."),

            new("sessions", "جلسات اللعب + Stutter Black Box", "Game Sessions / Stutter Black Box", D7FeatureState.Ready,
                "يسجل جلسات اللعبة تلقائيًا، FPS/1%/P99/Temps/RAM/Ping، ويرصد stutter ويحفظ تقرير JSON محلي."),

            new("storage", "التخزين والأقراص", "Storage Intelligence", D7FeatureState.Ready,
                "جرد الأقراص/المساحة/الصحة المتاحة من Windows مع واجهة Storage Center."),

            new("startup", "بدء التشغيل", "Startup Manager", D7FeatureState.Ready,
                "جرد وإدارة Startup entries مع مسارات حقيقية وسياسة آمنة."),

            new("background", "تطبيقات الخلفية", "Background App Manager", D7FeatureState.Ready,
                "CPU/RAM/Publisher classification + Protected/Keep/Review/SafeToClose + Smart Clean وسياسات مستخدم."),

            new("safe-update", "Update Everything Safe", "Update Everything Safe", D7FeatureState.Ready,
                "Winget apps + Windows Update drivers بعد backup/restore point. يمنع التنفيذ أثناء اللعب ولا يلمس BIOS/Firmware."),
        };
    }

    public static string StateArabic(D7FeatureState state) => state switch
    {
        D7FeatureState.Ready => "جاهز",
        D7FeatureState.Partial => "جزئي",
        D7FeatureState.ReadOnly => "قراءة فقط",
        D7FeatureState.Unavailable => "غير متاح حاليًا",
        _ => "قيد البناء"
    };

    private static string SafeName(Process process)
    {
        try { return process.ProcessName; }
        catch { return string.Empty; }
        finally { process.Dispose(); }
    }
}
