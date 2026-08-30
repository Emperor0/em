namespace D7SystemIntelligence.Core;

public enum D7RepairRoute
{
    None,
    WindowsRepair,
    TempCleanup,
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
        var area = (finding.Area ?? string.Empty).Trim();
        var title = (finding.Title ?? string.Empty).Trim();

        if (Contains(area, "تعريف", "driver") || Contains(title, "NVIDIA", "تعريف"))
            return Action(finding, "قابل للإجراء", "فتح Driver Safety Center", D7RepairRoute.DriverSafety, false);

        if (Contains(area, "بدء التشغيل", "startup"))
            return Action(finding, "قابل للإدارة", "إدارة بدء التشغيل", D7RepairRoute.StartupManager, false);

        if (Contains(area, "الذاكرة", "memory", "vram"))
            return Action(finding, "قابل للإدارة", "فتح تطبيقات الخلفية", D7RepairRoute.BackgroundApps, false);

        if (Contains(area, "التخزين", "storage", "disk"))
            return Action(finding, "قابل للفحص/الإصلاح", "فتح Storage Center", D7RepairRoute.StorageCenter, false);

        if (Contains(area, "الاستقرار", "stability") || Contains(title, "WHEA", "انهيار", "crash"))
            return Action(finding, "يحتاج تشخيص", "فتح Crash Investigator", D7RepairRoute.CrashInvestigator, false);

        if (Contains(area, "الحرارة", "thermal", "المراوح", "fans"))
            return Action(finding, "حسب دعم الهاردوير", "فتح التحكم الحراري", D7RepairRoute.FanControl, false);

        if (Contains(area, "الشبكة", "network"))
            return Action(finding, "قابل للفحص", "فتح مركز الشبكة", D7RepairRoute.NetworkCenter, false);

        if (finding.Severity.Contains("سليم", StringComparison.OrdinalIgnoreCase) ||
            finding.Severity.Contains("ok", StringComparison.OrdinalIgnoreCase))
            return new D7ActionableFinding(finding, false, false, "سليم", "لا يلزم إجراء", D7RepairRoute.None);

        return new D7ActionableFinding(finding, false, false, "مراقبة فقط", "لا يوجد إصلاح آلي آمن", D7RepairRoute.None);
    }

    public Task<string> RunAutomaticAsync(D7RepairRoute route, CancellationToken cancellationToken = default)
        => route switch
        {
            D7RepairRoute.WindowsRepair => SystemActions.RepairWindowsAsync(),
            D7RepairRoute.TempCleanup => SystemActions.CleanSafeTemporaryFilesAsync(cancellationToken),
            _ => Task.FromResult("هذا الإجراء يحتاج فتح مركزه المخصص بدل تنفيذ تعديل تلقائي غير آمن.")
        };

    public static D7ActionableFinding WindowsRepairCard()
        => Action(
            new DiagnosticFinding(
                "معلومة",
                "Windows",
                "سلامة ملفات Windows",
                "D7 يستطيع تشغيل DISM RestoreHealth ثم SFC /scannow وإظهار نتيجة الأداتين كاملة.",
                "استخدمه إذا ظهرت أخطاء نظام، ملفات تالفة أو مشاكل بعد تحديث."),
            "إصلاح فعلي",
            "إصلاح Windows الآن",
            D7RepairRoute.WindowsRepair,
            true);

    public static D7ActionableFinding TempCleanupCard()
        => Action(
            new DiagnosticFinding(
                "معلومة",
                "التنظيف",
                "تنظيف الملفات المؤقتة الآمن",
                "يحذف فقط ملفات Temp القديمة أكثر من 24 ساعة ويتخطى الملفات المستخدمة والحديثة.",
                "لا يحذف Downloads ولا ملفات المستخدم ولا يعطل خدمات Windows."),
            "إجراء آمن",
            "تنظيف الآن",
            D7RepairRoute.TempCleanup,
            true);

    private static D7ActionableFinding Action(
        DiagnosticFinding finding,
        string state,
        string label,
        D7RepairRoute route,
        bool automatic)
        => new(finding, true, automatic, state, label, route);

    private static bool Contains(string value, params string[] tokens)
        => tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
}
