using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace D7SystemIntelligence.Core;

public sealed record NetworkPropertyBackup(string RegistryKeyword, string[] RegistryValue, string DisplayName, string DisplayValue);
public sealed record NetworkProfileBackup(string AdapterName, DateTime SavedAtUtc, IReadOnlyList<NetworkPropertyBackup> Properties, string? AllowComputerToTurnOffDevice);
public sealed record NetworkProfileResult(bool Success, string AdapterName, int ChangedProperties, string Detail);

public sealed class NetworkGamingProfileService
{
    private readonly string _backupPath;

    public NetworkGamingProfileService()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D7SystemIntelligence", "RestoreVault");
        Directory.CreateDirectory(root);
        _backupPath = Path.Combine(root, "network-gaming-profile.json");
    }

    public async Task<NetworkProfileResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        var backup = await ReadCurrentAsync(cancellationToken);
        if (backup == null) return new NetworkProfileResult(false, string.Empty, 0, "تعذر تحديد محول الشبكة النشط أو قراءة خصائصه.");

        await File.WriteAllTextAsync(_backupPath, JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

        var targets = backup.Properties
            .Where(x => IsSafeLatencyPowerProperty(x.DisplayName, x.RegistryKeyword))
            .ToList();

        if (targets.Count == 0)
            return new NetworkProfileResult(true, backup.AdapterName, 0, "لم يجد D7 خصائص Energy/Green/Power Saving قابلة للتعديل على هذا المحول؛ لم يغير شيئًا.");

        var changed = 0;
        var messages = new List<string>();
        foreach (var property in targets)
        {
            var script = $@"
$ErrorActionPreference='Stop'
Set-NetAdapterAdvancedProperty -Name {Ps(backup.AdapterName)} -RegistryKeyword {Ps(property.RegistryKeyword)} -RegistryValue @('0') -NoRestart
";
            var result = await RunPowerShellAsync(script, cancellationToken);
            if (result.ExitCode == 0)
            {
                changed++;
                messages.Add($"{property.DisplayName}: {property.DisplayValue} → Disabled/0");
            }
            else
            {
                messages.Add($"{property.DisplayName}: لم يتغير ({Clean(result.Error)})");
            }
        }

        if (!string.IsNullOrWhiteSpace(backup.AllowComputerToTurnOffDevice) &&
            !backup.AllowComputerToTurnOffDevice.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
        {
            var power = await RunPowerShellAsync($@"
$ErrorActionPreference='Stop'
Set-NetAdapterPowerManagement -Name {Ps(backup.AdapterName)} -AllowComputerToTurnOffDevice Disabled -NoRestart
", cancellationToken);
            if (power.ExitCode == 0) messages.Add("Power Management: منع Windows من إطفاء المحول لتوفير الطاقة.");
        }

        if (changed > 0)
        {
            // One controlled adapter restart so vendor properties actually take effect.
            var restart = await RunPowerShellAsync($"Restart-NetAdapter -Name {Ps(backup.AdapterName)} -Confirm:$false", cancellationToken);
            messages.Add(restart.ExitCode == 0 ? "تمت إعادة تشغيل محول الشبكة مرة واحدة لتطبيق التغييرات." : "تعذر Restart للمحول؛ بعض الخصائص قد تحتاج إعادة اتصال/تشغيل لاحقًا.");
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
";
            var result = await RunPowerShellAsync(script, cancellationToken);
            if (result.ExitCode == 0)
            {
                restored++;
                messages.Add($"استعادة {property.DisplayName} → {property.DisplayValue}");
            }
            else messages.Add($"تعذر استعادة {property.DisplayName}: {Clean(result.Error)}");
        }

        if (!string.IsNullOrWhiteSpace(backup.AllowComputerToTurnOffDevice))
        {
            await RunPowerShellAsync($@"
$ErrorActionPreference='Stop'
Set-NetAdapterPowerManagement -Name {Ps(backup.AdapterName)} -AllowComputerToTurnOffDevice {backup.AllowComputerToTurnOffDevice}
", cancellationToken);
        }

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
