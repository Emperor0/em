using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace D7SystemIntelligence.Core;

public sealed record PowerPlanInfo(string Guid, string Name);
public sealed record PowerPlanResult(bool Success, string Detail, PowerPlanInfo? ActivePlan = null);

public sealed class PowerPlanService
{
    private const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
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
    {
        var current = await GetActiveAsync(cancellationToken);
        if (current == null) return new PowerPlanResult(false, "تعذر قراءة خطة الطاقة الحالية من Windows.");
        if (!File.Exists(_backupPath))
            await File.WriteAllTextAsync(_backupPath, JsonSerializer.Serialize(current, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

        if (current.Guid.Equals(HighPerformanceGuid, StringComparison.OrdinalIgnoreCase))
            return new PowerPlanResult(true, $"خطة الطاقة بالفعل High Performance • {current.Name}", current);

        var run = await RunAsync("powercfg.exe", $"/setactive {HighPerformanceGuid}", cancellationToken);
        if (run.ExitCode != 0)
            return new PowerPlanResult(false, "Windows رفض تفعيل High Performance.\n" + run.Output + "\n" + run.Error, current);

        var after = await GetActiveAsync(cancellationToken);
        return new PowerPlanResult(after?.Guid.Equals(HighPerformanceGuid, StringComparison.OrdinalIgnoreCase) == true,
            after == null ? "تم إرسال أمر High Performance لكن تعذر التحقق من الخطة النشطة." : $"تم تفعيل خطة الطاقة: {after.Name}", after);
    }

    public async Task<PowerPlanResult> ApplyBalancedAsync(CancellationToken cancellationToken = default)
    {
        var current = await GetActiveAsync(cancellationToken);
        if (current != null && !File.Exists(_backupPath))
            await File.WriteAllTextAsync(_backupPath, JsonSerializer.Serialize(current, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        var run = await RunAsync("powercfg.exe", "/setactive 381b4222-f694-41f0-9685-ff5bb260df2e", cancellationToken);
        var after = await GetActiveAsync(cancellationToken);
        return new PowerPlanResult(run.ExitCode == 0, after == null ? "تم طلب Balanced." : $"تم تفعيل خطة الطاقة: {after.Name}", after);
    }

    public async Task<PowerPlanResult> RestoreAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_backupPath)) return new PowerPlanResult(false, "لا توجد خطة طاقة محفوظة في Restore Vault.");
        PowerPlanInfo? original;
        try { original = JsonSerializer.Deserialize<PowerPlanInfo>(await File.ReadAllTextAsync(_backupPath, cancellationToken)); }
        catch (Exception ex) { return new PowerPlanResult(false, "تعذر قراءة خطة الطاقة المحفوظة: " + ex.Message); }
        if (original == null || string.IsNullOrWhiteSpace(original.Guid)) return new PowerPlanResult(false, "بيانات خطة الطاقة المحفوظة غير صالحة.");

        var run = await RunAsync("powercfg.exe", $"/setactive {original.Guid}", cancellationToken);
        var after = await GetActiveAsync(cancellationToken);
        if (run.ExitCode == 0)
        {
            try { File.Delete(_backupPath); } catch { }
            return new PowerPlanResult(true, after == null ? $"تمت استعادة خطة الطاقة {original.Name}." : $"تمت استعادة خطة الطاقة: {after.Name}", after);
        }
        return new PowerPlanResult(false, "فشل استرجاع خطة الطاقة الأصلية.\n" + run.Output + "\n" + run.Error, after);
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
