namespace D7SystemIntelligence.Core;

public sealed record FullHealthReport(
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<DiagnosticFinding> Diagnostics,
    StorageSnapshot Storage,
    CrashInvestigationReport Stability,
    string WindowsIntegrity,
    string Summary);

public sealed class FullHealthCheckService
{
    private readonly HardwareEngine _hardware;
    private readonly DiagnosticsEngine _diagnostics = new();
    private readonly StorageIntelligenceService _storage = new();
    private readonly CrashInvestigatorService _crashes = new();

    public FullHealthCheckService(HardwareEngine hardware) => _hardware = hardware;

    public async Task<FullHealthReport> RunAsync(bool includeWindowsIntegrity, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.Now;
        progress?.Report("قراءة حالة الهاردوير…");
        var hw = _hardware.Read();

        progress?.Report("تشغيل التشخيص الذكي…");
        var diagnostics = await _diagnostics.RunAsync(hw);
        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report("قراءة صحة التخزين وSMART/Reliability المتاح…");
        var storage = await _storage.ScanAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report("فحص أحداث الاستقرار لآخر 3 أيام…");
        var stability = await _crashes.ScanAsync(TimeSpan.FromDays(3), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var integrity = "لم يتم تشغيل فحص Windows Integrity في هذا الفحص.";
        if (includeWindowsIntegrity)
        {
            progress?.Report("تشغيل DISM ScanHealth + SFC VerifyOnly… قد يستغرق عدة دقائق.");
            integrity = await SystemActions.RunWindowsRepairScanAsync();
        }

        var critical = diagnostics.Count(x => x.Severity.Contains("حرج", StringComparison.OrdinalIgnoreCase));
        var warnings = diagnostics.Count(x => x.Severity.Contains("تحذير", StringComparison.OrdinalIgnoreCase));
        var storageWarnings = storage.Drives.Count(x => !x.HealthStatus.Equals("Healthy", StringComparison.OrdinalIgnoreCase)) + storage.Volumes.Count(x => x.FreePercent < 10);
        var stabilityImportant = stability.HardwareErrors + stability.GpuDriverEvents + stability.StorageEvents + stability.UnexpectedShutdowns;
        var summary = critical > 0 || stability.HardwareErrors > 0
            ? $"Health Check: يحتاج تدخل • Critical {critical} • Warnings {warnings} • Storage warnings {storageWarnings} • Stability events {stabilityImportant}"
            : warnings > 0 || storageWarnings > 0 || stabilityImportant > 0
                ? $"Health Check: توجد نقاط للمراجعة • Warnings {warnings} • Storage {storageWarnings} • Stability {stabilityImportant}"
                : "Health Check: لا توجد مشكلة حرجة ظاهرة في الفحوصات الحالية.";

        progress?.Report("اكتمل Full Health Check.");
        return new FullHealthReport(started, DateTimeOffset.Now, diagnostics, storage, stability, integrity, summary);
    }
}
