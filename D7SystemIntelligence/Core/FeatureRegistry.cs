using System.Diagnostics;

namespace D7SystemIntelligence.Core;

public enum D7FeatureState
{
    Ready,
    RuntimePending,
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
        var ffmpeg = new ManagedFfmpegService().Detect();
        var brightness = new DisplayControlService().ReadBrightness();

        return new List<D7FeatureCapability>
        {
            new("self-update", "تحديث D7KT الذاتي", "D7KT Self Update", D7FeatureState.RuntimePending,
                "Progress + SHA-256 + visible install + previous EXE backup + post-update shell/core healthcheck + executable rollback. يحتاج اختبار update/forced-rollback فعلي قبل Ready."),

            new("telemetry", "مراقبة الجهاز", "Hardware Telemetry",
                snapshot != null ? D7FeatureState.Ready : D7FeatureState.RuntimePending,
                "CPU/GPU/RAM/VRAM/temps/fans عبر LibreHardwareMonitor. Ready هنا يعني أن Snapshot حقيقية وصلت لهذه الجلسة."),

            new("missions", "Mission Control", "Mission Control", D7FeatureState.RuntimePending,
                "Applied/Verified/AlreadyOptimal/Unsupported/Failed + D7-owned restore. يحتاج اختبار Mission→game→restore على الجهاز."),

            new("auto-scene", "Auto Scene", "Auto Scene", D7FeatureState.RuntimePending,
                "Policy layer مدموج مع Missions، debounce/restore موجودان؛ يحتاج اختبار انتقالات ألعاب فعلية."),

            new("performance-contract", "Performance Contract", "Performance Contract", D7FeatureState.RuntimePending,
                "يعتمد Telemetry الجلسة المشتركة؛ سيُدمج وظيفيًا مع Missions/Benchmark بدل اعتباره Product pillar مستقل."),

            new("network", "Network Lab", "Network Lab", D7FeatureState.RuntimePending,
                "Gateway/Internet/DNS/remote-route classification + manual bufferbloat + verified NIC writes + Before/After + auto rollback on clear regression."),

            new("drivers", "Driver Safety", "Driver Safety", D7FeatureState.RuntimePending,
                "Driver Store backup + Restore Point attempt + Windows Update path + before/after inventory verification. لا توجد قاعدة newest=best."),

            new("peripherals", "Input Lab", "Peripheral / Input Lab", D7FeatureState.RuntimePending,
                "Physical HID grouping + Raw Input polling distribution/jitter/stalls + NKRO + XInput drift/range + Windows pointer backup/restore.", true),

            new("fans", "Smart Fans", "Smart Fans",
                controllableFans > 0 ? D7FeatureState.RuntimePending : D7FeatureState.ReadOnly,
                controllableFans > 0
                    ? $"ظهر {controllableFans} control writable حسب backend؛ D7KT ما يزال يحتاج write/read-back runtime test قبل Ready."
                    : "Monitor-only: لا توجد قناة writable معلنة. D7KT لا يستخدم EC/Super-I/O/PWM عشوائيًا.", true),

            new("shadow-capture", "D7 Shadow Capture", "D7 Shadow Capture",
                hasObs ? D7FeatureState.RuntimePending : D7FeatureState.Unavailable,
                hasObs
                    ? "OBS Replay ownership + no duplicate recorder + metadata/cleanup/impact test موجود؛ يحتاج حفظ Replay فعلي وقياس أثر."
                    : "OBS غير شغال حاليًا؛ Replay path لا يمكن اعتباره مختبرًا في هذه الجلسة.", true),

            new("clip-library", "مكتبة المقاطع", "Clip Library", D7FeatureState.RuntimePending,
                ffmpeg.Available
                    ? "إدارة المقاطع + trim backend متاح؛ الميزة مدموجة منطقيًا تحت Capture وتحتاج runtime file test."
                    : "إدارة الملفات موجودة؛ FFmpeg يجهز عند الحاجة. ستظل Clip Library جزءًا من Capture لا صفحة رئيسية."),

            new("stream-director", "Stream Director", "Stream Director",
                hasObs || hasTikTok ? D7FeatureState.RuntimePending : D7FeatureState.Unavailable,
                hasObs || hasTikTok
                    ? "OBS render/encode/network evidence + game telemetry correlation + TikTok/VirtualCam chain. يحتاج stream runtime test."
                    : "OBS/TikTok غير شغالين حاليًا."),

            new("apps", "البرامج الذكية", "App Intelligence", D7FeatureState.RuntimePending,
                "Discord/Steam/NVIDIA App/OBS/TikTok/Chrome/Edge: verified priorities, safe startup/cache, mission profiles, restore. Proprietary writes تبقى غير متاحة بدون adapter موثوق."),

            new("rgb", "RGB Studio", "RGB Studio",
                openRgb.Available ? D7FeatureState.RuntimePending : D7FeatureState.Unavailable,
                openRgb.Available
                    ? "Per-device mode/color/brightness/scenes + runtime intelligence عبر OpenRGB backend؛ يحتاج Device Matrix test."
                    : "OpenRGB غير مجهز/غير مكتشف؛ لا توجد RGB writes بدون backend حقيقي.", true),

            new("display", "Display Control", "Display Control", D7FeatureState.RuntimePending,
                brightness.Supported
                    ? "Hz validation/test/read-back/rollback + persistent Restore Vault + DDC/CI brightness verify. يحتاج monitor runtime test."
                    : "Hz path موجود مع verify/rollback؛ DDC/CI brightness غير متاح أو غير مثبت على الشاشة الحالية.", true),

            new("audio", "Audio Studio", "Audio Studio", D7FeatureState.RuntimePending,
                "Endpoint inventory + Volume/Mute + Console/Multimedia/Communications defaults مع read-back verify وRestore. Sample format/routing writes غير مدعاة."),

            new("overlay", "D7 HUD", "D7 HUD", D7FeatureState.RuntimePending,
                "Adaptive click-through HUD يستخدم RuntimeBus/GameSession telemetry المشتركة؛ لا يشغل PresentMon أو network scanner ثاني."),

            new("sessions", "Stutter Black Box", "Game Sessions / Stutter Black Box", D7FeatureState.RuntimePending,
                "Shared PresentMon session + raw frametimes/stutter evidence + reports. يحتاج جلسة لعبة فعلية."),

            new("benchmark", "Benchmark Lab", "Benchmark Lab", D7FeatureState.RuntimePending,
                "Raw FPS/1%/0.1%/P95/P99/P99.9 + confidence + KEEP/REJECT/NO PROOF. يحتاج A/B repeatability test."),

            new("storage", "Storage Center", "Storage Intelligence", D7FeatureState.RuntimePending,
                "Windows Storage Reliability + errors/temp/free + persistent delta/trend + Analyze/ReTrim بدون performance claim."),

            new("crash", "Crash Investigator", "Crash Investigator", D7FeatureState.RuntimePending,
                "WHEA/GPU/Storage/App/Kernel-Power evidence + temporal correlation chains؛ correlation ليست causation."),

            new("startup", "Startup Manager", "Startup Manager", D7FeatureState.RuntimePending,
                "إدارة حقيقية + Restore Vault؛ ستُدمج تحت Maintenance بدل صفحة رئيسية."),

            new("background", "Background Apps", "Background App Manager", D7FeatureState.RuntimePending,
                "Protected/Keep/Review/SafeToClose + user policy؛ ستندمج مع Maintenance/Missions ولا تعتبر generic process killer."),

            new("safe-update", "Maintenance", "Safe Maintenance", D7FeatureState.RuntimePending,
                "Scan→Plan→Apply، يمنع app/driver updates أثناء Gaming/Streaming، ولا يلمس BIOS/Firmware."),

            new("games", "Game Identity", "Game / Launcher Intelligence", D7FeatureState.RuntimePending,
                "Steam/Epic manifests when available + Xbox/Ubisoft/fallback + persistent user-confirmed executable identity. Heuristic EXE لا يعامل كحقيقة.")
        };
    }

    public static string StateArabic(D7FeatureState state) => state switch
    {
        D7FeatureState.Ready => "مُتحقق في الجلسة",
        D7FeatureState.RuntimePending => "الكود جاهز • اختبار الجهاز مطلوب",
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
