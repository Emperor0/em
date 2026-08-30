using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace D7SystemIntelligence.Core;

public sealed record PowerPlanInfo(string Guid, string Name);
public sealed record PowerPlanResult(bool Success, string Detail, PowerPlanInfo? ActivePlan = null, bool Changed = false);

public sealed class PowerPlanService
{
    private const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private readonly string _backupPath;

    public PowerPlanService()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D7SystemIntelligence", "RestoreVault");
        Directory.CreateDirectory(root);
        _backupPath = Path.Combine(root, "power-plan.json");
    }

    public async Task<PowerPlanInfo?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var run = await RunAsync("powercfg.exe", "/getactivescheme", cancellationToken);
        if (run.ExitCode != 0) return null;
        var match = Regex.Match(run.Output, @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s*\(([^)]*)\)");
        if (!match.Success) return null;
        return new PowerPlanInfo(match.Groups[1].Value.ToLowerInvariant(), match.Groups[2].Value.Trim());
    }

    public async Task<PowerPlanResult> ApplyHighPerformanceAsync(CancellationToken cancellationToken = default)
        => await ApplyPlanAsync(HighPerformanceGuid, "High Performance", cancellationToken);

    public async Task<PowerPlanResult> ApplyBalancedAsync(CancellationToken cancellationToken = default)
        => await ApplyPlanAsync(BalancedGuid, "Balanced", cancellationToken);

    private async Task<PowerPlanResult> ApplyPlanAsync(string targetGuid, string targetName, CancellationToken cancellationToken)
    {
        var current = await GetActiveAsync(cancellationToken);
        if (current == null) return new PowerPlanResult(false, "تعذر قراءة خطة الطاقة الحالية من Windows.");

        if (current.Guid.Equals(targetGuid, StringComparison.OrdinalIgnoreCase))
            return new PowerPlanResult(true, $"Already Optimal • خطة الطاقة بالفعل {current.Name}. لم يغير D7KT شيئًا.", current, false);

        await File.WriteAllTextAsync(_backupPath, JsonSerializer.Serialize(current, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

        var run = await RunAsync("powercfg.exe", $"/setactive {targetGuid}", cancellationToken);
        if (run.ExitCode != 0)
            return new PowerPlanResult(false, $"Windows رفض تفعيل {targetName}.\n{run.Output}\n{run.Error}", current, false);

        var after = await GetActiveAsync(cancellationToken);
        var verified = after?.Guid.Equals(targetGuid, StringComparison.OrdinalIgnoreCase) == true;
        if (!verified)
            return new PowerPlanResult(false, "تم إرسال أمر تغيير خطة الطاقة لكن التحقق بعد التطبيق لم يثبت الخطة المطلوبة.", after, false);

        return new PowerPlanResult(true, $"Applied + Verified • {current.Name} → {after!.Name}", after, true);
    }

    public async Task<PowerPlanResult> RestoreAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_backupPath)) return new PowerPlanResult(true, "لا يوجد تغيير Power Plan من D7KT يحتاج استعادة.", await GetActiveAsync(cancellationToken), false);
        PowerPlanInfo? original;
        try { original = JsonSerializer.Deserialize<PowerPlanInfo>(await File.ReadAllTextAsync(_backupPath, cancellationToken)); }
        catch (Exception ex) { return new PowerPlanResult(false, "تعذر قراءة خطة الطاقة المحفوظة: " + ex.Message); }
        if (original == null || string.IsNullOrWhiteSpace(original.Guid)) return new PowerPlanResult(false, "بيانات خطة الطاقة المحفوظة غير صالحة.");

        var current = await GetActiveAsync(cancellationToken);
        if (current?.Guid.Equals(original.Guid, StringComparison.OrdinalIgnoreCase) == true)
        {
            try { File.Delete(_backupPath); } catch { }
            return new PowerPlanResult(true, $"Restore Verified • الخطة أصلًا رجعت إلى {current.Name}.", current, false);
        }

        var run = await RunAsync("powercfg.exe", $"/setactive {original.Guid}", cancellationToken);
        var after = await GetActiveAsync(cancellationToken);
        var verified = run.ExitCode == 0 && after?.Guid.Equals(original.Guid, StringComparison.OrdinalIgnoreCase) == true;
        if (verified)
        {
            try { File.Delete(_backupPath); } catch { }
            return new PowerPlanResult(true, $"Restore Verified • تمت استعادة خطة الطاقة: {after!.Name}", after, true);
        }
        return new PowerPlanResult(false, "فشل التحقق من استرجاع خطة الطاقة الأصلية.\n" + run.Output + "\n" + run.Error, after, false);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(string file, string arguments, CancellationToken cancellationToken)
    {
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo(file, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        p.Start();
        var stdout = p.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = p.StandardError.ReadToEndAsync(cancellationToken);
        await p.WaitForExitAsync(cancellationToken);
        return (p.ExitCode, (await stdout).Trim(), (await stderr).Trim());
    }
}
