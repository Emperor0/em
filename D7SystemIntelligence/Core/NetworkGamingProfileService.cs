using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace D7SystemIntelligence.Core;

public sealed record NetworkPropertyBackup(string RegistryKeyword, string[] RegistryValue, string DisplayName, string DisplayValue);
public sealed record NetworkProfileBackup(string AdapterName, DateTime SavedAtUtc, IReadOnlyList<NetworkPropertyBackup> Properties, string? AllowComputerToTurnOffDevice);
public sealed record NetworkProfileResult(bool Success, string AdapterName, int ChangedProperties, string Detail);
public sealed record NetworkMeasuredProfileResult(
    bool Success,
    bool Kept,
    NetworkReport Before,
    NetworkReport After,
    NetworkProfileResult ApplyResult,
    string Verdict);

public sealed class NetworkGamingProfileService
{
    private readonly string _backupPath;

    public NetworkGamingProfileService()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D7SystemIntelligence", "RestoreVault");
        Directory.CreateDirectory(root);
        _backupPath = Path.Combine(root, "network-gaming-profile.json");
    }

    public async Task<NetworkMeasuredProfileResult> ApplyMeasuredAsync(
        NetworkIntelligence intelligence,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("قياس الشبكة قبل التعديل…");
        var before = await intelligence.ScanAsync(cancellationToken);

        progress?.Report("تطبيق خصائص NIC الآمنة والتحقق منها…");
        var apply = await ApplyAsync(cancellationToken);
        if (!apply.Success || apply.ChangedProperties == 0)
        {
            var afterNoChange = await intelligence.ScanAsync(cancellationToken);
            return new NetworkMeasuredProfileResult(
                apply.Success,
                apply.Success,
                before,
                afterNoChange,
                apply,
                apply.Success ? "لم توجد تغييرات NIC مفيدة قابلة للتطبيق؛ D7KT لم يغيّر الشبكة." : apply.Detail);
        }

        progress?.Report("انتظار استقرار الاتصال ثم القياس بعد التعديل…");
        await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
        var after = await intelligence.ScanAsync(cancellationToken);

        var regression = IsClearRegression(before, after, out var reason);
        if (!regression)
        {
            var verdict = BuildComparison(before, after, true);
            return new NetworkMeasuredProfileResult(true, true, before, after, apply, verdict);
        }

        progress?.Report("ظهر تدهور واضح؛ استعادة إعدادات NIC السابقة…");
        var restore = await RestoreAsync(cancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
        var restored = await intelligence.ScanAsync(cancellationToken);
        var rollbackVerdict = $"REJECT + ROLLBACK • {reason}\n{BuildComparison(before, after, false)}\nRestore: {restore.Detail}\nبعد الاستعادة: {Summary(restored)}";
        return new NetworkMeasuredProfileResult(false, false, before, restored, apply, rollbackVerdict);
    }

    public async Task<NetworkProfileResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        var backup = await ReadCurrentAsync(cancellationToken);
        if (backup == null) return new NetworkProfileResult(false, string.Empty, 0, "تعذر تحديد محول الشبكة النشط أو قراءة خصائصه.");

        await File.WriteAllTextAsync(_backupPath, JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

        var targets = backup.Properties.Where(x => IsSafeLatencyPowerProperty(x.DisplayName, x.RegistryKeyword)).ToList();
        var changed = 0;
        var messages = new List<string>();

        foreach (var property in targets)
        {
            var script = $@"
$ErrorActionPreference='Stop'
Set-NetAdapterAdvancedProperty -Name {Ps(backup.AdapterName)} -RegistryKeyword {Ps(property.RegistryKeyword)} -RegistryValue @('0') -NoRestart
$verify = Get-NetAdapterAdvancedProperty -Name {Ps(backup.AdapterName)} -RegistryKeyword {Ps(property.RegistryKeyword)} -ErrorAction Stop
[pscustomobject]@{{ RegistryValue=@($verify.RegistryValue | ForEach-Object {{$_.ToString()}}); DisplayValue=$verify.DisplayValue }} | ConvertTo-Json -Compress
";
            var result = await RunPowerShellAsync(script, cancellationToken);
            if (result.ExitCode != 0)
            {
                messages.Add($"{property.DisplayName}: فشل ({Clean(result.Error)})");
                continue;
            }

            if (VerifyZero(result.Output, out var verifiedDisplay))
            {
                changed++;
                messages.Add($"{property.DisplayName}: {property.DisplayValue} → {verifiedDisplay} [Verified]");
            }
            else
            {
                messages.Add($"{property.DisplayName}: أمر الكتابة نجح لكن القراءة لم تثبت القيمة 0؛ لم يحسب D7KT التغيير.");
            }
        }

        var powerChanged = false;
        if (!string.IsNullOrWhiteSpace(backup.AllowComputerToTurnOffDevice) &&
            !backup.AllowComputerToTurnOffDevice.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
        {
            var power = await RunPowerShellAsync($@"
$ErrorActionPreference='Stop'
Set-NetAdapterPowerManagement -Name {Ps(backup.AdapterName)} -AllowComputerToTurnOffDevice Disabled -NoRestart
$pm=Get-NetAdapterPowerManagement -Name {Ps(backup.AdapterName)} -ErrorAction Stop
$pm.AllowComputerToTurnOffDevice.ToString()
", cancellationToken);
            if (power.ExitCode == 0 && power.Output.Contains("Disabled", StringComparison.OrdinalIgnoreCase))
            {
                powerChanged = true;
                changed++;
                messages.Add("Power Management: AllowComputerToTurnOffDevice → Disabled [Verified]");
            }
            else if (power.ExitCode != 0)
            {
                messages.Add("Power Management: تعذر التغيير — " + Clean(power.Error));
            }
        }

        if (targets.Count == 0 && !powerChanged)
            return new NetworkProfileResult(true, backup.AdapterName, 0, "لم يجد D7KT خصائص Energy/Green/Power Saving قابلة للتعديل؛ لم يغير شيئًا.");

        if (changed > 0)
        {
            var restart = await RunPowerShellAsync($"Restart-NetAdapter -Name {Ps(backup.AdapterName)} -Confirm:$false", cancellationToken);
            messages.Add(restart.ExitCode == 0
                ? "تم Restart واحد للمحول لأن تغييرًا Verified يحتاج إعادة تهيئة."
                : "تعذر Restart للمحول؛ بعض الخصائص قد تحتاج إعادة اتصال/تشغيل لاحقًا.");
        }

        return new NetworkProfileResult(true, backup.AdapterName, changed, string.Join(Environment.NewLine, messages));
    }

    public async Task<NetworkProfileResult> RestoreAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_backupPath))
            return new NetworkProfileResult(false, string.Empty, 0, "لا توجد نسخة Network Restore محفوظة.");

        NetworkProfileBackup? backup;
        try
        {
            backup = JsonSerializer.Deserialize<NetworkProfileBackup>(await File.ReadAllTextAsync(_backupPath, cancellationToken));
        }
        catch (Exception ex)
        {
            return new NetworkProfileResult(false, string.Empty, 0, "تعذر قراءة النسخة الاحتياطية: " + ex.Message);
        }
        if (backup == null) return new NetworkProfileResult(false, string.Empty, 0, "ملف الاستعادة فارغ.");

        var restored = 0;
        var messages = new List<string>();
        foreach (var property in backup.Properties.Where(x => IsSafeLatencyPowerProperty(x.DisplayName, x.RegistryKeyword)))
        {
            var values = string.Join(',', property.RegistryValue.Select(Ps));
            var script = $@"
$ErrorActionPreference='Stop'
Set-NetAdapterAdvancedProperty -Name {Ps(backup.AdapterName)} -RegistryKeyword {Ps(property.RegistryKeyword)} -RegistryValue @({values}) -NoRestart
$verify = Get-NetAdapterAdvancedProperty -Name {Ps(backup.AdapterName)} -RegistryKeyword {Ps(property.RegistryKeyword)} -ErrorAction Stop
@($verify.RegistryValue | ForEach-Object {{$_.ToString()}}) -join ','
";
            var result = await RunPowerShellAsync(script, cancellationToken);
            if (result.ExitCode == 0 && SameValues(property.RegistryValue, result.Output))
            {
                restored++;
                messages.Add($"استعادة {property.DisplayName} → {property.DisplayValue} [Verified]");
            }
            else
            {
                messages.Add($"تعذر إثبات استعادة {property.DisplayName}: {Clean(result.Error + " " + result.Output)}");
            }
        }

        if (!string.IsNullOrWhiteSpace(backup.AllowComputerToTurnOffDevice))
        {
            var power = await RunPowerShellAsync($@"
$ErrorActionPreference='Stop'
Set-NetAdapterPowerManagement -Name {Ps(backup.AdapterName)} -AllowComputerToTurnOffDevice {backup.AllowComputerToTurnOffDevice} -NoRestart
$pm=Get-NetAdapterPowerManagement -Name {Ps(backup.AdapterName)} -ErrorAction Stop
$pm.AllowComputerToTurnOffDevice.ToString()
", cancellationToken);
            messages.Add(power.ExitCode == 0 && power.Output.Contains(backup.AllowComputerToTurnOffDevice, StringComparison.OrdinalIgnoreCase)
                ? $"استعادة Power Management → {backup.AllowComputerToTurnOffDevice} [Verified]"
                : "تعذر إثبات استعادة Power Management.");
        }

        if (restored > 0 || !string.IsNullOrWhiteSpace(backup.AllowComputerToTurnOffDevice))
            await RunPowerShellAsync($"Restart-NetAdapter -Name {Ps(backup.AdapterName)} -Confirm:$false", cancellationToken);

        return new NetworkProfileResult(true, backup.AdapterName, restored, string.Join(Environment.NewLine, messages));
    }

    public async Task<NetworkProfileBackup?> ReadCurrentAsync(CancellationToken cancellationToken = default)
    {
        const string script = @"
$route = Get-NetRoute -AddressFamily IPv4 -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue | Sort-Object RouteMetric,InterfaceMetric | Select-Object -First 1
if(-not $route){ exit 2 }
$adapter = Get-NetAdapter -InterfaceIndex $route.InterfaceIndex -ErrorAction Stop
$props = Get-NetAdapterAdvancedProperty -Name $adapter.Name -ErrorAction SilentlyContinue | ForEach-Object {
    [pscustomobject]@{
        RegistryKeyword = $_.RegistryKeyword
        RegistryValue = @($_.RegistryValue | ForEach-Object { $_.ToString() })
        DisplayName = $_.DisplayName
        DisplayValue = $_.DisplayValue
    }
}
$pm = Get-NetAdapterPowerManagement -Name $adapter.Name -ErrorAction SilentlyContinue
[pscustomobject]@{
    AdapterName = $adapter.Name
    AllowComputerToTurnOffDevice = if($pm){$pm.AllowComputerToTurnOffDevice.ToString()}else{$null}
    Properties = @($props)
} | ConvertTo-Json -Depth 6 -Compress
";

        var run = await RunPowerShellAsync(script, cancellationToken);
        if (run.ExitCode != 0 || string.IsNullOrWhiteSpace(run.Output)) return null;

        try
        {
            using var doc = JsonDocument.Parse(run.Output);
            var root = doc.RootElement;
            var adapter = root.GetProperty("AdapterName").GetString() ?? string.Empty;
            string? power = null;
            if (root.TryGetProperty("AllowComputerToTurnOffDevice", out var pm) && pm.ValueKind == JsonValueKind.String) power = pm.GetString();

            var props = new List<NetworkPropertyBackup>();
            if (root.TryGetProperty("Properties", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in arr.EnumerateArray())
                {
                    var keyword = GetString(p, "RegistryKeyword");
                    var displayName = GetString(p, "DisplayName");
                    var displayValue = GetString(p, "DisplayValue");
                    var values = new List<string>();
                    if (p.TryGetProperty("RegistryValue", out var rv))
                    {
                        if (rv.ValueKind == JsonValueKind.Array) values.AddRange(rv.EnumerateArray().Select(x => x.ToString()));
                        else if (rv.ValueKind != JsonValueKind.Null) values.Add(rv.ToString());
                    }
                    if (!string.IsNullOrWhiteSpace(keyword)) props.Add(new NetworkPropertyBackup(keyword, values.ToArray(), displayName, displayValue));
                }
            }
            return new NetworkProfileBackup(adapter, DateTime.UtcNow, props, power);
        }
        catch { return null; }
    }

    private static bool VerifyZero(string json, out string displayValue)
    {
        displayValue = "0";
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("DisplayValue", out var d)) displayValue = d.ToString();
            if (!root.TryGetProperty("RegistryValue", out var values)) return false;
            if (values.ValueKind == JsonValueKind.Array) return values.EnumerateArray().Any(x => x.ToString() == "0");
            return values.ToString() == "0";
        }
        catch { return false; }
    }

    private static bool SameValues(IEnumerable<string> expected, string actual)
    {
        var a = expected.Select(x => x.Trim()).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var b = (actual ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        return a.SequenceEqual(b, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsClearRegression(NetworkReport before, NetworkReport after, out string reason)
    {
        reason = string.Empty;
        if (after.PacketLossPercent >= 5 && after.PacketLossPercent >= before.PacketLossPercent + 3)
        {
            reason = $"Packet loss ارتفع {before.PacketLossPercent:0.#}% → {after.PacketLossPercent:0.#}%";
            return true;
        }
        if (before.InternetLatencyMs.HasValue && after.InternetLatencyMs.HasValue &&
            after.InternetLatencyMs.Value >= before.InternetLatencyMs.Value + Math.Max(15, before.InternetLatencyMs.Value * .35))
        {
            reason = $"Latency ارتفع {before.InternetLatencyMs:0.0}ms → {after.InternetLatencyMs:0.0}ms";
            return true;
        }
        if (before.JitterMs.HasValue && after.JitterMs.HasValue &&
            after.JitterMs.Value >= before.JitterMs.Value + Math.Max(8, before.JitterMs.Value * .75))
        {
            reason = $"Jitter ارتفع {before.JitterMs:0.0}ms → {after.JitterMs:0.0}ms";
            return true;
        }
        return false;
    }

    private static string BuildComparison(NetworkReport before, NetworkReport after, bool kept)
    {
        static string V(double? x) => x.HasValue ? $"{x.Value:0.0}ms" : "—";
        return $"{(kept ? "KEEP" : "REJECT")} • Before: Ping {V(before.InternetLatencyMs)} / Jitter {V(before.JitterMs)} / Loss {before.PacketLossPercent:0.#}% → After: Ping {V(after.InternetLatencyMs)} / Jitter {V(after.JitterMs)} / Loss {after.PacketLossPercent:0.#}%";
    }

    private static string Summary(NetworkReport r)
        => $"Ping {(r.InternetLatencyMs.HasValue ? r.InternetLatencyMs.Value.ToString("0.0") + "ms" : "—")} • Jitter {(r.JitterMs.HasValue ? r.JitterMs.Value.ToString("0.0") + "ms" : "—")} • Loss {r.PacketLossPercent:0.#}%";

    private static bool IsSafeLatencyPowerProperty(string displayName, string keyword)
    {
        var text = (displayName + " " + keyword).ToLowerInvariant();
        return text.Contains("energy efficient") || text.Contains("green ethernet") || text.Contains("power saving") ||
               text.Contains("gigabit lite") || text.Contains("eee") || text.Contains("reduce speed on power down");
    }

    private static string GetString(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind != JsonValueKind.Null ? p.ToString() : string.Empty;

    private static string Ps(string value) => "'" + (value ?? string.Empty).Replace("'", "''") + "'";
    private static string Clean(string value) => string.Join(' ', (value ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static async Task<(int ExitCode, string Output, string Error)> RunPowerShellAsync(string script, CancellationToken cancellationToken)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        if (p == null) return (-1, string.Empty, "PowerShell لم يبدأ.");
        var stdout = p.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = p.StandardError.ReadToEndAsync(cancellationToken);
        await p.WaitForExitAsync(cancellationToken);
        return (p.ExitCode, (await stdout).Trim(), (await stderr).Trim());
    }
}
