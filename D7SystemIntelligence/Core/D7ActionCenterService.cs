namespace D7SystemIntelligence.Core;

public enum D7RepairRoute
{
    None,
    WindowsRepair,
    TempCleanup,
    DiskScan,
    StorageCenter,
    DriverSafety,
    StartupManager,
    BackgroundApps,
    CrashInvestigator,
    RestoreVault,
    FanControl,
    NetworkCenter
}

public sealed record D7ActionableFinding(
    DiagnosticFinding Finding,
    bool CanAct,
    bool AutomaticRepair,
    string State,
    string ActionLabel,
    D7RepairRoute Route);

public sealed class D7ActionCenterService
{
    public IReadOnlyList<D7ActionableFinding> Classify(IEnumerable<DiagnosticFinding> findings)
        => findings.Select(Classify).ToArray();

    public D7ActionableFinding Classify(DiagnosticFinding finding)
    {
        return finding.Code switch
        {
            "THERMAL_CPU_CRITICAL" or "THERMAL_CPU_WARNING" or "THERMAL_GPU_WARNING"
                => Action(finding, "إجراء مشروط بالدعم", "فتح التحكم الحراري", D7RepairRoute.FanControl, false),

            "RAM_PRESSURE"
                => Action(finding, "قابل للإدارة", "فحص تطبيقات الخلفية", D7RepairRoute.BackgroundApps, false),

            "VRAM_PRESSURE"
                => NoAuto(finding, "لا يوجد Fix عام آمن", "VRAM ضغط خاص بالمشهد/اللعبة؛ يلزم Game Profile أو إعدادات الخامات بدل إغلاق عمليات عشوائيًا."),

            "WHEA_RECENT"
                => Action(finding, "تشخيص قبل التعديل", "فتح Crash Investigator", D7RepairRoute.CrashInvestigator, false),

            "NVIDIA_DRIVER_EVENTS"
                => Action(finding, "تحقق من الاستقرار/التعريف", "فتح Driver Safety", D7RepairRoute.DriverSafety, false),

            "DISK_EVENT_ERRORS"
                => Action(finding, "فحص آمن أولًا", "تشغيل CHKDSK /scan", D7RepairRoute.DiskScan, true),

            "DISK_LOW_SPACE"
                => Action(finding, "تنظيف محدود وآمن", "تنظيف Temp القديمة", D7RepairRoute.TempCleanup, true),

            "STARTUP_HIGH_COUNT"
                => Action(finding, "مراجعة يدوية موصى بها", "فتح Startup Manager", D7RepairRoute.StartupManager, false),

            "EVENTLOG_UNAVAILABLE"
                => NoAuto(finding, "Evidence ناقص", "أعد تشغيل D7KT كمسؤول أو تحقق من Windows Event Log. لا يوجد إصلاح آلي بدون معرفة سبب المنع."),

            "HEALTH_OK"
                => new D7ActionableFinding(finding, false, false, "سليم", "لا يلزم إجراء", D7RepairRoute.None),

            _ => ClassifyLegacy(finding)
        };
    }

    public Task<string> RunAutomaticAsync(D7RepairRoute route, CancellationToken cancellationToken = default)
        => route switch
        {
            D7RepairRoute.WindowsRepair => SystemActions.RepairWindowsAsync(),
            D7RepairRoute.TempCleanup => SystemActions.CleanSafeTemporaryFilesAsync(cancellationToken),
            D7RepairRoute.DiskScan => SystemActions.CheckSystemDriveAsync(),
            _ => Task.FromResult("هذا الإجراء يحتاج مركزه المخصص؛ D7KT رفض تنفيذ تعديل عام غير آمن.")
        };

    public static D7ActionableFinding WindowsRepairCard()
        => Action(
            new DiagnosticFinding(
                "معلومة",
                "Windows",
                "سلامة ملفات Windows",
                "DISM RestoreHealth ثم SFC /scannow مع عرض النتيجة الكاملة.",
                "شغله عند وجود دليل على تلف Windows أو بعد فشل ScanHealth، وليس كـFPS tweak.",
                "WINDOWS_REPAIR_MANUAL"),
            "إصلاح فعلي عند الحاجة",
            "إصلاح Windows الآن",
            D7RepairRoute.WindowsRepair,
            true);

    public static D7ActionableFinding TempCleanupCard()
        => Action(
            new DiagnosticFinding(
                "معلومة",
                "التنظيف",
                "تنظيف الملفات المؤقتة الآمن",
                "يحذف Temp أقدم من 24 ساعة ويتخطى الملفات المستخدمة والحديثة.",
                "لا يلمس Downloads أو ملفات المستخدم.",
                "TEMP_CLEANUP_MANUAL"),
            "إجراء محدود وآمن",
            "تنظيف الآن",
            D7RepairRoute.TempCleanup,
            true);

    private static D7ActionableFinding ClassifyLegacy(DiagnosticFinding finding)
    {
        var area = (finding.Area ?? string.Empty).Trim();
        var title = (finding.Title ?? string.Empty).Trim();

        if (Contains(area, "تعريف", "driver") || Contains(title, "NVIDIA", "تعريف"))
            return Action(finding, "قابل للإجراء", "فتح Driver Safety", D7RepairRoute.DriverSafety, false);
        if (Contains(area, "بدء التشغيل", "startup"))
            return Action(finding, "قابل للإدارة", "فتح Startup Manager", D7RepairRoute.StartupManager, false);
        if (Contains(area, "الذاكرة", "memory") && !Contains(area, "vram", "ذاكرة الكرت"))
            return Action(finding, "قابل للإدارة", "فتح Background Apps", D7RepairRoute.BackgroundApps, false);
        if (Contains(area, "التخزين", "storage", "disk"))
            return Action(finding, "قابل للفحص", "فتح Storage Center", D7RepairRoute.StorageCenter, false);
        if (Contains(area, "الاستقرار", "stability") || Contains(title, "WHEA", "انهيار", "crash"))
            return Action(finding, "يحتاج Evidence", "فتح Crash Investigator", D7RepairRoute.CrashInvestigator, false);
        if (Contains(area, "الحرارة", "thermal", "المراوح", "fans"))
            return Action(finding, "حسب دعم الهاردوير", "فتح التحكم الحراري", D7RepairRoute.FanControl, false);
        if (Contains(area, "الشبكة", "network"))
            return Action(finding, "قابل للفحص", "فتح مركز الشبكة", D7RepairRoute.NetworkCenter, false);
        if (finding.Severity.Contains("سليم", StringComparison.OrdinalIgnoreCase) || finding.Severity.Contains("ok", StringComparison.OrdinalIgnoreCase))
            return new D7ActionableFinding(finding, false, false, "سليم", "لا يلزم إجراء", D7RepairRoute.None);

        return NoAuto(finding, "مراقبة فقط", "لا يوجد مسار إصلاح آلي موثوق لهذا Finding حتى الآن.");
    }

    private static D7ActionableFinding Action(
        DiagnosticFinding finding,
        string state,
        string label,
        D7RepairRoute route,
        bool automatic)
        => new(finding, true, automatic, state, label, route);

    private static D7ActionableFinding NoAuto(DiagnosticFinding finding, string state, string explanation)
    {
        var enriched = finding with
        {
            Recommendation = string.IsNullOrWhiteSpace(finding.Recommendation)
                ? explanation
                : finding.Recommendation + " " + explanation
        };
        return new D7ActionableFinding(enriched, false, false, state, "لا يوجد إصلاح آلي آمن", D7RepairRoute.None);
    }

    private static bool Contains(string value, params string[] tokens)
        => tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
}
