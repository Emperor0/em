namespace D7SystemIntelligence.Core;

public sealed record SafeMaintenanceResult(bool Success, string Detail, bool RebootRequired);
public sealed record MaintenancePlan(
    bool AllowedNow,
    string GuardReason,
    string AppScan,
    IReadOnlyList<WindowsDriverUpdate> DriverUpdates,
    string Summary);

public sealed class SafeMaintenanceService
{
    private readonly DriverSafetyService _drivers = new();

    public async Task<MaintenancePlan> BuildPlanAsync(CancellationToken cancellationToken = default)
    {
        var context = D7RuntimeBus.Context;
        var blocked = context?.Mode is D7RuntimeMode.Gaming or D7RuntimeMode.StreamGaming or D7RuntimeMode.Streaming;
        var guard = blocked
            ? $"مؤجل: D7KT رصد {context!.Mode} {(string.IsNullOrWhiteSpace(context.PrimaryGame) ? string.Empty : "• " + context.PrimaryGame)}. لا يشغل Updates ثقيلة أثناء اللعب/البث."
            : "مسموح الآن: لا توجد جلسة لعب/بث نشطة حسب Runtime Engine.";

        var appScan = await SystemActions.RunWingetUpgradeScanAsync();
        cancellationToken.ThrowIfCancellationRequested();
        var drivers = await _drivers.ScanWindowsUpdateDriversAsync(cancellationToken);
        var summary = $"Apps: تم فحص winget • Driver Updates: {drivers.Count} • Apply {(blocked ? "BLOCKED" : "AVAILABLE")}";
        return new MaintenancePlan(!blocked, guard, appScan, drivers, summary);
    }

    public async Task<SafeMaintenanceResult> RunUpdatesAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var plan = await BuildPlanAsync(cancellationToken);
        if (!plan.AllowedNow)
            return new SafeMaintenanceResult(false, plan.GuardReason + "\nلم يغيّر D7KT أي شيء.", false);

        var messages = new List<string> { "=== PLAN ===\n" + plan.Summary };
        var success = true;
        var reboot = false;

        progress?.Report("تحديث التطبيقات عبر Winget…");
        try
        {
            var apps = await SystemActions.UpgradeAppsAsync();
            messages.Add("=== Apps / Winget ===\n" + Trim(apps, 12000));
        }
        catch (Exception ex)
        {
            success = false;
            messages.Add("Winget فشل: " + ex.Message);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (plan.DriverUpdates.Count > 0)
        {
            progress?.Report("Driver Store Backup → Restore Point → Windows Update Drivers → Verify…");
            try
            {
                var driverResult = await _drivers.InstallWindowsUpdateDriversAsync(cancellationToken);
                success &= driverResult.Success;
                reboot |= driverResult.RebootRequired;
                messages.Add("=== Drivers ===\n" + driverResult.Detail);
            }
            catch (Exception ex)
            {
                success = false;
                messages.Add("Driver Update فشل: " + ex.Message);
            }
        }
        else
        {
            messages.Add("=== Drivers ===\nNO ACTION • لا توجد Driver Updates عبر Windows Update؛ لم ينشئ D7KT تغييرًا بلا داعٍ.");
        }

        progress?.Report("اكتمل Maintenance Apply.");
        messages.Add("\nD7KT لم يلمس BIOS/Firmware، ولم يعتبر عدم وجود تحديثات تحسينًا. تحديث D7KT نفسه يبقى عبر Self Update + SHA-256.");
        return new SafeMaintenanceResult(success, string.Join("\n\n", messages), reboot);
    }

    public async Task<string> ScanOnlyAsync(CancellationToken cancellationToken = default)
    {
        var plan = await BuildPlanAsync(cancellationToken);
        var lines = new List<string>
        {
            plan.GuardReason,
            plan.Summary,
            "",
            "=== Apps ===",
            Trim(plan.AppScan, 9000),
            "",
            $"=== Driver Updates ({plan.DriverUpdates.Count}) ==="
        };
        lines.AddRange(plan.DriverUpdates.Select(x => "• " + x.Title));
        return string.Join(Environment.NewLine, lines);
    }

    private static string Trim(string value, int max)
        => string.IsNullOrWhiteSpace(value) ? "لا يوجد إخراج." : value.Length <= max ? value : value[..max] + "\n… output truncated …";
}
