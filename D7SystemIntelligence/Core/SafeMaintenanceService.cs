namespace D7SystemIntelligence.Core;

public sealed record SafeMaintenanceResult(bool Success, string Detail, bool RebootRequired);

public sealed class SafeMaintenanceService
{
    private readonly DriverSafetyService _drivers = new();

    public async Task<SafeMaintenanceResult> RunUpdatesAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var messages = new List<string>();
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
        progress?.Report("أخذ Driver Store backup ثم فحص/تثبيت تعريفات Windows Update…");
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

        progress?.Report("اكتمل Update Everything Safe.");
        messages.Add("\nD7 لم يلمس BIOS/Firmware أو تحديثًا حساسًا خارج Windows Update. تحديث D7 نفسه يبقى عبر زر تحديث D7 الموثق بـSHA-256.");
        return new SafeMaintenanceResult(success, string.Join("\n\n", messages), reboot);
    }

    public async Task<string> ScanOnlyAsync(CancellationToken cancellationToken = default)
    {
        var apps = await SystemActions.RunWingetUpgradeScanAsync();
        var drivers = await _drivers.ScanWindowsUpdateDriversAsync(cancellationToken);
        var lines = new List<string> { "=== Apps ===", Trim(apps, 9000), "", $"=== Driver Updates ({drivers.Count}) ===" };
        lines.AddRange(drivers.Select(x => "• " + x.Title));
        return string.Join(Environment.NewLine, lines);
    }

    private static string Trim(string value, int max)
        => string.IsNullOrWhiteSpace(value) ? "لا يوجد إخراج." : value.Length <= max ? value : value[..max] + "\n… output truncated …";
}
