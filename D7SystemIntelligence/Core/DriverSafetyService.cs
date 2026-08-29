using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace D7SystemIntelligence.Core;

public sealed record WindowsDriverUpdate(string Title, string Description, bool Downloaded);
public sealed record DriverOperationResult(bool Success, string Detail, string? BackupPath = null, bool RebootRequired = false);

public sealed class DriverSafetyService
{
    private readonly string _vaultRoot;

    public DriverSafetyService()
    {
        _vaultRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D7SystemIntelligence", "RestoreVault", "Drivers");
        Directory.CreateDirectory(_vaultRoot);
    }

    public async Task<DriverOperationResult> BackupDriverStoreAsync(CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(_vaultRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);
        var run = await RunProcessAsync("pnputil.exe", $"/export-driver * \"{folder}\"", cancellationToken);
        var infCount = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.inf", SearchOption.AllDirectories).Count() : 0;
        if (run.ExitCode != 0 || infCount == 0)
            return new DriverOperationResult(false, $"فشل تصدير Driver Store. ExitCode={run.ExitCode}\n{run.Output}\n{run.Error}", folder);

        return new DriverOperationResult(true, $"تم تصدير {infCount} حزمة تعريف إلى Restore Vault.\n{folder}", folder);
    }

    public async Task<IReadOnlyList<WindowsDriverUpdate>> ScanWindowsUpdateDriversAsync(CancellationToken cancellationToken = default)
    {
        const string script = @"
$session = New-Object -ComObject Microsoft.Update.Session
$searcher = $session.CreateUpdateSearcher()
$result = $searcher.Search(""IsInstalled=0 and Type='Driver' and IsHidden=0"")
$rows = @()
foreach($u in $result.Updates){
  $rows += [pscustomobject]@{Title=$u.Title;Description=$u.Description;Downloaded=$u.IsDownloaded}
}
$rows | ConvertTo-Json -Depth 4 -Compress
";
        var run = await RunPowerShellAsync(script, cancellationToken);
        if (run.ExitCode != 0 || string.IsNullOrWhiteSpace(run.Output)) return [];
        try
        {
            using var doc = JsonDocument.Parse(run.Output);
            var list = new List<WindowsDriverUpdate>();
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in doc.RootElement.EnumerateArray()) list.Add(ParseUpdate(e));
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                list.Add(ParseUpdate(doc.RootElement));
            }
            return list;
        }
        catch { return []; }
    }

    public async Task<DriverOperationResult> InstallWindowsUpdateDriversAsync(CancellationToken cancellationToken = default)
    {
        var backup = await BackupDriverStoreAsync(cancellationToken);
        if (!backup.Success)
            return new DriverOperationResult(false, "D7 رفض تثبيت تعريفات قبل نجاح النسخة الاحتياطية.\n" + backup.Detail, backup.BackupPath);

        var restorePoint = await TryCreateRestorePointAsync(cancellationToken);
        const string script = @"
$ErrorActionPreference='Stop'
$session = New-Object -ComObject Microsoft.Update.Session
$searcher = $session.CreateUpdateSearcher()
$result = $searcher.Search(""IsInstalled=0 and Type='Driver' and IsHidden=0"")
if($result.Updates.Count -eq 0){ Write-Output '{"Installed":0,"ResultCode":0,"RebootRequired":false,"Titles":[]}' ; exit 0 }
$collection = New-Object -ComObject Microsoft.Update.UpdateColl
$titles = @()
foreach($u in $result.Updates){
  if(-not $u.EulaAccepted){ try{$u.AcceptEula()}catch{} }
  [void]$collection.Add($u)
  $titles += $u.Title
}
$downloader = $session.CreateUpdateDownloader()
$downloader.Updates = $collection
$downloadResult = $downloader.Download()
$installer = $session.CreateUpdateInstaller()
$installer.Updates = $collection
$installResult = $installer.Install()
[pscustomobject]@{
  Installed=$collection.Count
  ResultCode=[int]$installResult.ResultCode
  RebootRequired=[bool]$installResult.RebootRequired
  Titles=$titles
} | ConvertTo-Json -Depth 5 -Compress
";
        var run = await RunPowerShellAsync(script, cancellationToken);
        if (run.ExitCode != 0)
            return new DriverOperationResult(false, $"Windows Update Driver install فشل.\n{run.Error}\nBackup: {backup.BackupPath}\nRestore point: {restorePoint}", backup.BackupPath);

        try
        {
            using var doc = JsonDocument.Parse(run.Output);
            var installed = doc.RootElement.GetProperty("Installed").GetInt32();
            var code = doc.RootElement.GetProperty("ResultCode").GetInt32();
            var reboot = doc.RootElement.GetProperty("RebootRequired").GetBoolean();
            var titles = new List<string>();
            if (doc.RootElement.TryGetProperty("Titles", out var t) && t.ValueKind == JsonValueKind.Array)
                titles.AddRange(t.EnumerateArray().Select(x => x.ToString()));
            var success = code is 0 or 2 or 3; // NotStarted/Success/SuccessWithErrors semantics from WU API; 2/3 are usable outcomes.
            var detail = installed == 0
                ? "لا توجد Driver Updates متاحة عبر Windows Update."
                : $"Windows Update عالج {installed} تعريف/تعريفات. ResultCode={code}.\n" + string.Join(Environment.NewLine, titles.Select(x => "• " + x));
            detail += $"\n\nBackup: {backup.BackupPath}\nRestore point: {restorePoint}";
            return new DriverOperationResult(success, detail, backup.BackupPath, reboot);
        }
        catch
        {
            return new DriverOperationResult(false, "التثبيت رجع نتيجة غير قابلة للقراءة.\n" + run.Output + "\nBackup: " + backup.BackupPath, backup.BackupPath);
        }
    }

    public async Task<DriverOperationResult> RestoreExportedDriversAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(backupPath) || !Directory.Exists(backupPath))
            return new DriverOperationResult(false, "مجلد Backup غير موجود.");

        var infs = Directory.EnumerateFiles(backupPath, "*.inf", SearchOption.AllDirectories).ToArray();
        if (infs.Length == 0) return new DriverOperationResult(false, "لا توجد ملفات INF داخل Backup.");

        var run = await RunProcessAsync("pnputil.exe", $"/add-driver \"{Path.Combine(backupPath, "*.inf")}\" /subdirs /install", cancellationToken);
        var ok = run.ExitCode == 0;
        return new DriverOperationResult(ok,
            (ok ? "تمت إعادة إضافة حزم Backup إلى Driver Store وطلب تثبيت الأنسب للأجهزة الحالية." : "pnputil لم يكمل الاستعادة بنجاح.") +
            $"\nمهم: Windows PnP قد يرفض downgrade إذا رأى التعريف الحالي أعلى ranking.\n{run.Output}\n{run.Error}", backupPath);
    }

    public IReadOnlyList<string> ListBackups()
    {
        if (!Directory.Exists(_vaultRoot)) return [];
        return Directory.EnumerateDirectories(_vaultRoot)
            .OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static WindowsDriverUpdate ParseUpdate(JsonElement e)
    {
        static string S(JsonElement el, string name) => el.TryGetProperty(name, out var p) ? p.ToString() : string.Empty;
        var downloaded = e.TryGetProperty("Downloaded", out var d) && d.ValueKind == JsonValueKind.True;
        return new WindowsDriverUpdate(S(e, "Title"), S(e, "Description"), downloaded);
    }

    private static async Task<string> TryCreateRestorePointAsync(CancellationToken cancellationToken)
    {
        const string script = @"
try {
  Checkpoint-Computer -Description 'D7 Before Driver Update' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop
  'Created'
} catch { 'Unavailable: ' + $_.Exception.Message }
";
        var run = await RunPowerShellAsync(script, cancellationToken);
        return string.IsNullOrWhiteSpace(run.Output) ? "Unavailable" : run.Output.Trim();
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunPowerShellAsync(string script, CancellationToken cancellationToken)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return await RunProcessAsync("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}", cancellationToken);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunProcessAsync(string file, string arguments, CancellationToken cancellationToken)
    {
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = file,
                Arguments = arguments,
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
