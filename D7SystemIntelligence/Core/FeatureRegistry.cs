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
            .Select(p => SafeName(p))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var hasObs = processes.Any(x => x.Contains("obs", StringComparison.OrdinalIgnoreCase));
        var hasTikTok = processes.Any(x => x.Contains("tiktok", StringComparison.OrdinalIgnoreCase));
        var ffmpeg = FindExecutable("ffmpeg.exe");
        var openRgb = FindExecutable("OpenRGB.exe");

        var controllableFans = snapshot?.Fans.Count(f => f.Controllable) ?? 0;

        return new List<D7FeatureCapability>
        {
            new("self-update", "تحديث D7 الذاتي", "D7 Self Update", D7FeatureState.Ready,
                "GitHub Release + SHA-256 + تثبيت صامت وإعادة تشغيل."),
            new("telemetry", "مراقبة الجهاز", "Hardware Telemetry", D7FeatureState.Ready,
                "CPU/GPU/RAM/VRAM والحرارة والمراوح المتاحة."),
            new("network", "ذكاء الشبكة", "Network Intelligence", D7FeatureState.Ready,
                "Ping/Jitter/Loss/Gateway/Link diagnostics."),
            new("drivers", "ذكاء التعريفات", "Driver Intelligence", D7FeatureState.Partial,
                "الجرد يعمل؛ المقارنة الرسمية والتثبيت/الرجوع تحت البناء."),
            new("peripherals", "الأجهزة الطرفية", "Peripheral Intelligence", D7FeatureState.Partial,
                "جرد HID يعمل؛ تجميع الأجهزة والـPolling/Deadzone تحت البناء.", true),
            new("fans", "المراوح الذكية", "Smart Fans", controllableFans > 0 ? D7FeatureState.Partial : D7FeatureState.ReadOnly,
                controllableFans > 0
                    ? $"تم اكتشاف {controllableFans} قناة قابلة للكتابة؛ Auto Curve تحت البناء."
                    : "قراءة RPM فقط؛ لم يثبت وجود قناة كتابة آمنة على هذا الجهاز.", true),
            new("shadow-capture", "D7 Shadow Capture", "D7 Shadow Capture",
                ffmpeg != null || hasObs ? D7FeatureState.Partial : D7FeatureState.Unavailable,
                ffmpeg != null
                    ? $"تم اكتشاف FFmpeg: {ffmpeg}. محرك Replay الدائري قيد الربط."
                    : hasObs
                        ? "OBS موجود ويمكن استخدامه كBackend عند تفعيل تكامل Replay."
                        : "لا يوجد Backend تسجيل مثبت حاليًا؛ لن يعرض D7 زر تسجيل وهمي.", true),
            new("stream-director", "مدير البث", "Stream Director", hasObs || hasTikTok ? D7FeatureState.Partial : D7FeatureState.Planned,
                hasObs || hasTikTok ? "تم اكتشاف برنامج بث؛ الربط العميق مع الإحصاءات قيد البناء." : "سيعمل عند اكتشاف OBS/TikTok LIVE Studio."),
            new("rgb", "استوديو RGB", "RGB Studio", openRgb != null ? D7FeatureState.Partial : D7FeatureState.Unavailable,
                openRgb != null ? $"تم اكتشاف OpenRGB: {openRgb}." : "OpenRGB/واجهة جهاز مدعومة غير متاحة حاليًا.", true),
            new("display", "ذكاء الشاشة", "Display Intelligence", D7FeatureState.Partial,
                "اكتشاف الشاشة موجود عبر الأجهزة؛ Refresh/VRR/HDR/DDC controls تحت البناء.", true),
            new("audio", "ذكاء الصوت", "Audio Intelligence", D7FeatureState.Partial,
                "اكتشاف أجهزة الصوت موجود؛ Routing/DPC/Profiles تحت البناء.", true),
            new("overlay", "D7 HUD", "D7 Overlay", D7FeatureState.Planned,
                "سيعرض FPS/1%/frametime/temps/ping بشكل صغير وقابل للإخفاء."),
            new("sessions", "جلسات اللعب", "Game Sessions", D7FeatureState.Partial,
                "Telemetry history موجود؛ تقارير الجلسات والـmarkers تحت البناء."),
        };
    }

    public static string StateArabic(D7FeatureState state) => state switch
    {
        D7FeatureState.Ready => "جاهز",
        D7FeatureState.Partial => "جزئي",
        D7FeatureState.ReadOnly => "قراءة فقط",
        D7FeatureState.Unavailable => "غير متاح",
        _ => "قيد البناء"
    };

    private static string SafeName(Process process)
    {
        try { return process.ProcessName; }
        catch { return string.Empty; }
        finally { process.Dispose(); }
    }

    private static string? FindExecutable(string fileName)
    {
        var candidates = new List<string>();
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        candidates.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => Path.Combine(p.Trim(), fileName)));

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        candidates.Add(Path.Combine(programFiles, "OpenRGB", fileName));
        candidates.Add(Path.Combine(programFilesX86, "OpenRGB", fileName));
        candidates.Add(Path.Combine(programFiles, "ffmpeg", "bin", fileName));

        return candidates.FirstOrDefault(File.Exists);
    }
}
