using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace D7SystemIntelligence.Core;

public enum RemovalVerdict
{
    Blocked,
    HighRisk,
    Review,
    Safe
}

public sealed record InstalledAppRecord(
    string RegistryId,
    string DisplayName,
    string DisplayVersion,
    string Publisher,
    string InstallLocation,
    string UninstallString,
    string QuietUninstallString,
    bool WindowsInstaller,
    bool SystemComponent);

public sealed record RemovalAnalysis(
    string TargetPath,
    string TargetType,
    RemovalVerdict Verdict,
    string VerdictText,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> LockingProcesses,
    InstalledAppRecord? InstalledApp)
{
    public bool CanRemove => Verdict != RemovalVerdict.Blocked;
}

public sealed class QuarantineRecord
{
    public string Id { get; set; } = string.Empty;
    public string OriginalPath { get; set; } = string.Empty;
    public string QuarantinedPath { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
}

public sealed class SmartRemovalService
{
    private readonly string _vaultRoot;
    private readonly string _manifestPath;
    private readonly StartupManagerService _startup = new();

    private static readonly string[] ProtectedAppTokens =
    {
        "microsoft visual c++", ".net runtime", ".net host", "windows desktop runtime", "webview2 runtime",
        "windows update", "security update", "servicing stack", "amd chipset", "nvidia graphics driver",
        "realtek audio driver", "intel chipset", "microsoft edge webview2", "windows app runtime"
    };

    public SmartRemovalService()
    {
        _vaultRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D7SystemIntelligence", "RestoreVault", "Removal");
        Directory.CreateDirectory(_vaultRoot);
        _manifestPath = Path.Combine(_vaultRoot, "quarantine-manifest.json");
    }

    public IReadOnlyList<InstalledAppRecord> ScanInstalledApps()
    {
        var list = new List<InstalledAppRecord>();
        ScanApps(list, RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM64");
        ScanApps(list, RegistryHive.LocalMachine, RegistryView.Registry32, "HKLM32");
        ScanApps(list, RegistryHive.CurrentUser, RegistryView.Default, "HKCU");

        return list
            .Where(x => !string.IsNullOrWhiteSpace(x.DisplayName))
            .GroupBy(x => $"{x.DisplayName}|{x.InstallLocation}|{x.UninstallString}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<RemovalAnalysis> AnalyzePathAsync(string input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Blocked(string.Empty, "غير معروف", "لم تدخل مسارًا.");

        string full;
        try { full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(input.Trim().Trim('"'))); }
        catch { return Blocked(input, "غير معروف", "المسار غير صالح."); }

        var isFile = File.Exists(full);
        var isDirectory = Directory.Exists(full);
        if (!isFile && !isDirectory)
            return Blocked(full, "غير موجود", "الملف أو المجلد غير موجود.");

        var reasons = new List<string>();
        var verdict = RemovalVerdict.Safe;
        var type = isDirectory ? "مجلد" : "ملف";

        if (IsDriveRoot(full)) return Blocked(full, type, "D7 يمنع حذف جذر أي قرص.");
        if (IsProtectedShellRoot(full)) return Blocked(full, type, "D7 يمنع حذف مجلد مستخدم أساسي نفسه مثل Desktop/Documents/Downloads/Profile.");
        if (IsProtectedSystemPath(full)) return Blocked(full, type, "المسار داخل منطقة Windows/WindowsApps/ProgramData Microsoft المحمية.");
        if (IsD7Path(full)) return Blocked(full, type, "D7 يمنع حذف نفسه أو Restore Vault/Quarantine من داخل أداة الحذف.");

        if (isFile && IsWrpProtected(full))
            return Blocked(full, type, "Windows Resource Protection صنّف الملف كملف نظام محمي.");

        var info = isDirectory ? (FileSystemInfo)new DirectoryInfo(full) : new FileInfo(full);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            verdict = RemovalVerdict.HighRisk;
            reasons.Add("الهدف Reparse Point/Junction/Symbolic Link؛ لا يسمح D7 بحذف نهائي مباشر له.");
        }

        var app = FindOwningApp(full);
        if (app != null)
        {
            reasons.Add($"مرتبط ببرنامج مثبت: {app.DisplayName} {app.DisplayVersion}".Trim());
            if (IsProtectedDependency(app))
                return Blocked(full, type, "الهدف تابع لتعريف/Runtime/مكوّن نظام يعتمد عليه برامج أخرى.");
        }

        if (IsUnder(full, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)) ||
            IsUnder(full, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)) ||
            IsUnder(full, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)))
        {
            if (verdict == RemovalVerdict.Safe) verdict = RemovalVerdict.Review;
            reasons.Add("الهدف داخل Program Files/ProgramData؛ يحتاج مراجعة قبل الإزالة.");
        }
        else
        {
            reasons.Add("الهدف خارج مناطق النظام المحمية.");
        }

        var locks = await GetLockingProcessesAsync(full, isDirectory, cancellationToken);
        if (locks.Count > 0)
        {
            if (verdict == RemovalVerdict.Safe) verdict = RemovalVerdict.Review;
            reasons.Add("الهدف مستخدم حاليًا بواسطة عملية/عمليات؛ D7 لن يجبر الحذف وهي مفتوحة.");
        }

        return new RemovalAnalysis(full, type, verdict, VerdictArabic(verdict), reasons, locks, app);
    }

    public async Task<RemovalAnalysis> AnalyzeInstalledAppAsync(InstalledAppRecord app, CancellationToken cancellationToken = default)
    {
        if (IsProtectedDependency(app))
            return new RemovalAnalysis(app.InstallLocation, "برنامج", RemovalVerdict.Blocked, VerdictArabic(RemovalVerdict.Blocked),
                new[] { "D7 صنف هذا البرنامج كتعريف/Runtime/مكوّن مشترك حساس ولن يحذفه من Root Remover." }, Array.Empty<string>(), app);

        var reasons = new List<string>
        {
            "سيتم تشغيل UninstallString/Windows Installer أولًا بدل حذف الملفات بالقوة."
        };
        if (!string.IsNullOrWhiteSpace(app.InstallLocation)) reasons.Add("بعد نجاح الإزالة يمكن فحص InstallLocation كبقايا.");
        if (string.IsNullOrWhiteSpace(app.UninstallString) && string.IsNullOrWhiteSpace(app.QuietUninstallString))
            reasons.Add("لا يوجد UninstallString واضح؛ D7 لن يخترع أمر إزالة.");

        IReadOnlyList<string> locks = Array.Empty<string>();
        if (!string.IsNullOrWhiteSpace(app.InstallLocation) && Directory.Exists(app.InstallLocation))
            locks = await GetLockingProcessesAsync(app.InstallLocation, true, cancellationToken);

        var verdict = string.IsNullOrWhiteSpace(app.UninstallString) && string.IsNullOrWhiteSpace(app.QuietUninstallString)
            ? RemovalVerdict.Review
            : RemovalVerdict.Safe;
        return new RemovalAnalysis(app.InstallLocation, "برنامج", verdict, VerdictArabic(verdict), reasons, locks, app);
    }

    public async Task<string> UninstallAppAsync(InstalledAppRecord app, bool deepCleanup, CancellationToken cancellationToken = default)
    {
        if (IsProtectedDependency(app)) return "D7 رفض الإزالة: البرنامج مصنف Dependency/Driver/System Component حساس.";
        var command = BuildUninstallCommand(app);
        if (command == null) return "لا يوجد UninstallString صالح؛ D7 لن يحذف مجلد البرنامج مباشرة وكأنه Uninstaller.";

        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = command.Value.File,
                Arguments = command.Value.Arguments,
                UseShellExecute = true,
                WorkingDirectory = !string.IsNullOrWhiteSpace(app.InstallLocation) && Directory.Exists(app.InstallLocation)
                    ? app.InstallLocation
                    : Environment.GetFolderPath(Environment.SpecialFolder.System)
            });
            if (p == null) return "تعذر تشغيل أداة الإزالة.";
            await p.WaitForExitAsync(cancellationToken);
            var code = p.ExitCode;
            var ok = code is 0 or 1605 or 1641 or 3010;
            if (!ok) return $"أداة الإزالة انتهت برمز {code}. D7 لم يحذف البقايا بالقوة.";
        }
        catch (Exception ex)
        {
            return "فشل تشغيل Uninstaller: " + ex.Message;
        }

        await Task.Delay(1200, cancellationToken);
        var notes = new List<string> { $"تم تشغيل إزالة {app.DisplayName} بنجاح." };

        if (!string.IsNullOrWhiteSpace(app.InstallLocation))
        {
            foreach (var entry in _startup.Scan().Where(x => x.Enabled && CommandPointsInto(x.Command, app.InstallLocation)).ToArray())
            {
                try { notes.Add(_startup.Disable(entry.Id)); } catch { }
            }

            if (deepCleanup && Directory.Exists(app.InstallLocation) && IsExclusiveInstallDirectory(app))
            {
                var analysis = await AnalyzePathAsync(app.InstallLocation, cancellationToken);
                if (analysis.CanRemove && analysis.LockingProcesses.Count == 0 && analysis.Verdict != RemovalVerdict.HighRisk)
                    notes.Add(await QuarantineAsync(analysis, cancellationToken));
                else
                    notes.Add("وجد D7 بقايا في InstallLocation لكنه لم ينقلها لأنها تحتاج مراجعة/مقفلة/عالية الخطورة.");
            }
            else if (Directory.Exists(app.InstallLocation))
            {
                notes.Add("ما زال InstallLocation موجودًا. فعّل Deep Cleanup بعد مراجعة المجلد إذا أردت نقله إلى Quarantine.");
            }
        }

        return string.Join(Environment.NewLine, notes);
    }

    public async Task<string> QuarantineAsync(RemovalAnalysis analysis, CancellationToken cancellationToken = default)
    {
        if (!analysis.CanRemove) return "D7 رفض الإزالة: الهدف محمي.";
        if (analysis.LockingProcesses.Count > 0) return "الهدف مستخدم الآن بواسطة: " + string.Join("، ", analysis.LockingProcesses);
        if (analysis.Verdict == RemovalVerdict.HighRisk) return "Reparse Point/Junction لا ينتقل إلى Quarantine تلقائيًا؛ افحص الهدف يدويًا أولًا.";

        var full = analysis.TargetPath;
        if (!File.Exists(full) && !Directory.Exists(full)) return "الهدف لم يعد موجودًا.";
        var driveRoot = Path.GetPathRoot(full);
        if (string.IsNullOrWhiteSpace(driveRoot)) return "تعذر تحديد القرص.";
        var quarantineRoot = Path.Combine(driveRoot, ".D7Quarantine");
        Directory.CreateDirectory(quarantineRoot);
        try { File.SetAttributes(quarantineRoot, File.GetAttributes(quarantineRoot) | FileAttributes.Hidden); } catch { }

        var name = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var destination = Path.Combine(quarantineRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "_" + Guid.NewGuid().ToString("N") + "_" + Sanitize(name));

        try
        {
            if (File.Exists(full)) File.Move(full, destination);
            else Directory.Move(full, destination);
        }
        catch (Exception ex)
        {
            return "تعذر نقل الهدف إلى Quarantine. غالبًا ملف مقفول أو صلاحية خاصة: " + ex.Message;
        }

        var records = LoadManifest();
        var record = new QuarantineRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            OriginalPath = full,
            QuarantinedPath = destination,
            TargetType = analysis.TargetType,
            CreatedUtc = DateTime.UtcNow
        };
        records.Add(record);
        SaveManifest(records);
        await Task.CompletedTask;
        return $"تمت إزالة الهدف من مكانه الأصلي إلى D7 Quarantine مع إمكانية الاستعادة.\nID: {record.Id}";
    }

    public async Task<string> PermanentDeleteAsync(RemovalAnalysis analysis, CancellationToken cancellationToken = default)
    {
        if (analysis.Verdict != RemovalVerdict.Safe)
            return "الحذف النهائي مسموح فقط للأهداف المصنفة Safe. استخدم Quarantine للعناصر Review/HighRisk.";
        if (analysis.LockingProcesses.Count > 0)
            return "تعذر الحذف النهائي لأن الهدف مستخدم بواسطة: " + string.Join("، ", analysis.LockingProcesses);

        try
        {
            if (File.Exists(analysis.TargetPath)) File.Delete(analysis.TargetPath);
            else if (Directory.Exists(analysis.TargetPath)) Directory.Delete(analysis.TargetPath, recursive: true);
            else return "الهدف غير موجود.";
            await Task.CompletedTask;
            return "تم الحذف النهائي للهدف المصنف Safe.";
        }
        catch (Exception ex) { return "فشل الحذف النهائي: " + ex.Message; }
    }

    public IReadOnlyList<QuarantineRecord> ListQuarantine()
        => LoadManifest().OrderByDescending(x => x.CreatedUtc).ToArray();

    public string RestoreQuarantine(string id)
    {
        var records = LoadManifest();
        var record = records.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (record == null) return "عنصر Quarantine غير موجود.";
        if (!File.Exists(record.QuarantinedPath) && !Directory.Exists(record.QuarantinedPath)) return "بيانات Quarantine نفسها غير موجودة.";
        if (File.Exists(record.OriginalPath) || Directory.Exists(record.OriginalPath)) return "المسار الأصلي مستخدم حاليًا؛ D7 لن يستبدله.";
        Directory.CreateDirectory(Path.GetDirectoryName(record.OriginalPath)!);
        try
        {
            if (File.Exists(record.QuarantinedPath)) File.Move(record.QuarantinedPath, record.OriginalPath);
            else Directory.Move(record.QuarantinedPath, record.OriginalPath);
            records.Remove(record);
            SaveManifest(records);
            return "تمت استعادة العنصر إلى مكانه الأصلي.";
        }
        catch (Exception ex) { return "تعذرت الاستعادة: " + ex.Message; }
    }

    public string PurgeQuarantine(string id)
    {
        var records = LoadManifest();
        var record = records.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (record == null) return "عنصر Quarantine غير موجود.";
        try
        {
            if (File.Exists(record.QuarantinedPath)) File.Delete(record.QuarantinedPath);
            else if (Directory.Exists(record.QuarantinedPath)) Directory.Delete(record.QuarantinedPath, true);
            records.Remove(record);
            SaveManifest(records);
            return "تم حذف نسخة Quarantine نهائيًا.";
        }
        catch (Exception ex) { return "تعذر حذف Quarantine: " + ex.Message; }
    }

    private InstalledAppRecord? FindOwningApp(string path)
    {
        return ScanInstalledApps()
            .Where(x => !string.IsNullOrWhiteSpace(x.InstallLocation) && IsUnder(path, x.InstallLocation))
            .OrderByDescending(x => x.InstallLocation.Length)
            .FirstOrDefault();
    }

    private bool IsExclusiveInstallDirectory(InstalledAppRecord app)
    {
        if (string.IsNullOrWhiteSpace(app.InstallLocation) || !Directory.Exists(app.InstallLocation)) return false;
        var same = ScanInstalledApps().Count(x => !string.IsNullOrWhiteSpace(x.InstallLocation) &&
            PathEquals(x.InstallLocation, app.InstallLocation));
        if (same > 1) return false;
        var dirName = Normalize(Path.GetFileName(app.InstallLocation.TrimEnd(Path.DirectorySeparatorChar)));
        var appName = Normalize(app.DisplayName);
        return dirName.Length >= 4 && (appName.Contains(dirName, StringComparison.OrdinalIgnoreCase) || dirName.Contains(appName, StringComparison.OrdinalIgnoreCase));
    }

    private static (string File, string Arguments)? BuildUninstallCommand(InstalledAppRecord app)
    {
        var raw = !string.IsNullOrWhiteSpace(app.QuietUninstallString) ? app.QuietUninstallString : app.UninstallString;
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var guid = Regex.Match(raw, @"\{[0-9A-Fa-f-]{36}\}");
        if (app.WindowsInstaller || raw.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
        {
            if (!guid.Success) return null;
            return ("msiexec.exe", $"/x {guid.Value} /passive /norestart");
        }

        raw = Environment.ExpandEnvironmentVariables(raw.Trim());
        if (raw.StartsWith('"'))
        {
            var end = raw.IndexOf('"', 1);
            if (end <= 1) return null;
            return (raw[1..end], raw[(end + 1)..].Trim());
        }

        var split = raw.IndexOf(' ');
        return split < 0 ? (raw, string.Empty) : (raw[..split], raw[(split + 1)..].Trim());
    }

    private static bool IsProtectedDependency(InstalledAppRecord app)
    {
        if (app.SystemComponent) return true;
        var text = (app.DisplayName + " " + app.Publisher).ToLowerInvariant();
        return ProtectedAppTokens.Any(text.Contains);
    }

    private static void ScanApps(List<InstalledAppRecord> list, RegistryHive hive, RegistryView view, string prefix)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var root = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", false);
            if (root == null) return;
            foreach (var subName in root.GetSubKeyNames())
            {
                try
                {
                    using var key = root.OpenSubKey(subName, false);
                    if (key == null) continue;
                    var display = key.GetValue("DisplayName")?.ToString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(display)) continue;
                    var systemComponent = Convert.ToInt32(key.GetValue("SystemComponent", 0)) == 1;
                    list.Add(new InstalledAppRecord(
                        prefix + "|" + subName,
                        display,
                        key.GetValue("DisplayVersion")?.ToString() ?? string.Empty,
                        key.GetValue("Publisher")?.ToString() ?? string.Empty,
                        Environment.ExpandEnvironmentVariables(key.GetValue("InstallLocation")?.ToString() ?? string.Empty).Trim().Trim('"'),
                        key.GetValue("UninstallString")?.ToString() ?? string.Empty,
                        key.GetValue("QuietUninstallString")?.ToString() ?? string.Empty,
                        Convert.ToInt32(key.GetValue("WindowsInstaller", 0)) == 1,
                        systemComponent));
                }
                catch { }
            }
        }
        catch { }
    }

    private static RemovalAnalysis Blocked(string path, string type, string reason)
        => new(path, type, RemovalVerdict.Blocked, VerdictArabic(RemovalVerdict.Blocked), new[] { reason }, Array.Empty<string>(), null);

    private static string VerdictArabic(RemovalVerdict verdict) => verdict switch
    {
        RemovalVerdict.Blocked => "محمي — ممنوع الحذف",
        RemovalVerdict.HighRisk => "خطورة عالية",
        RemovalVerdict.Review => "يحتاج مراجعة",
        _ => "آمن للحذف"
    };

    private static bool IsDriveRoot(string path)
    {
        var root = Path.GetPathRoot(path);
        return !string.IsNullOrWhiteSpace(root) && PathEquals(root, path);
    }

    private static bool IsProtectedSystemPath(string path)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var windowsApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
        var programDataMicrosoft = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft");
        return IsUnder(path, windows) || IsUnder(path, windowsApps) || IsUnder(path, programDataMicrosoft);
    }

    private bool IsD7Path(string path)
    {
        var current = AppContext.BaseDirectory;
        var localD7 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D7SystemIntelligence");
        return IsUnder(path, current) || IsUnder(path, localD7) || IsUnder(current, path);
    }

    private static bool IsProtectedShellRoot(string path)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
        }.Where(x => !string.IsNullOrWhiteSpace(x));
        return roots.Any(x => PathEquals(path, x));
    }

    private static bool IsWrpProtected(string file)
    {
        try { return SfcIsFileProtected(IntPtr.Zero, file); }
        catch { return false; }
    }

    private static bool CommandPointsInto(string command, string installLocation)
        => !string.IsNullOrWhiteSpace(command) && !string.IsNullOrWhiteSpace(installLocation) &&
           command.Contains(installLocation.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private static bool IsUnder(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return false;
        try
        {
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return full.Equals(basePath, StringComparison.OrdinalIgnoreCase) ||
                   full.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool PathEquals(string a, string b)
    {
        try { return Path.GetFullPath(a).TrimEnd('\', '/').Equals(Path.GetFullPath(b).TrimEnd('\', '/'), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static string Normalize(string text)
        => Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9]+", string.Empty);

    private static string Sanitize(string name)
        => string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

    private List<QuarantineRecord> LoadManifest()
    {
        try
        {
            if (!File.Exists(_manifestPath)) return [];
            return JsonSerializer.Deserialize<List<QuarantineRecord>>(File.ReadAllText(_manifestPath)) ?? [];
        }
        catch { return []; }
    }

    private void SaveManifest(List<QuarantineRecord> records)
        => File.WriteAllText(_manifestPath, JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));

    private static async Task<IReadOnlyList<string>> GetLockingProcessesAsync(string path, bool directory, CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!directory)
        {
            foreach (var name in RestartManagerInspector.GetLockingProcessNames(path)) names.Add(name);
        }
        else
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    var exe = p.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(exe) && IsUnder(exe, path)) names.Add(p.ProcessName);
                }
                catch { }
                finally { p.Dispose(); }
            }
        }
        return names.OrderBy(x => x).ToArray();
    }

    [DllImport("sfc.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SfcIsFileProtected(IntPtr RpcHandle, string ProtFileName);
}

internal static class RestartManagerInspector
{
    private const int ErrorMoreData = 234;
    private const int CchRmSessionKey = 32;

    public static IReadOnlyList<string> GetLockingProcessNames(string filePath)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        uint handle = 0;
        var key = new StringBuilder(CchRmSessionKey + 1);
        if (RmStartSession(out handle, 0, key) != 0) return result.ToArray();
        try
        {
            var resources = new[] { filePath };
            if (RmRegisterResources(handle, (uint)resources.Length, resources, 0, IntPtr.Zero, 0, null) != 0) return result.ToArray();
            uint needed = 0, count = 0, reasons = 0;
            var code = RmGetList(handle, out needed, ref count, null, ref reasons);
            if (code == 0 || code != ErrorMoreData || needed == 0) return result.ToArray();
            var apps = new RM_PROCESS_INFO[needed];
            count = needed;
            code = RmGetList(handle, out needed, ref count, apps, ref reasons);
            if (code != 0) return result.ToArray();
            for (var i = 0; i < count; i++)
            {
                var app = apps[i];
                if (!string.IsNullOrWhiteSpace(app.strAppName)) result.Add(app.strAppName);
                else if (app.Process.dwProcessId > 0)
                {
                    try { using var p = Process.GetProcessById(app.Process.dwProcessId); result.Add(p.ProcessName); } catch { }
                }
            }
            return result.OrderBy(x => x).ToArray();
        }
        finally { RmEndSession(handle); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public FILETIME ProcessStartTime;
    }

    private enum RM_APP_TYPE
    {
        RmUnknownApp = 0,
        RmMainWindow = 1,
        RmOtherWindow = 2,
        RmService = 3,
        RmExplorer = 4,
        RmConsole = 5,
        RmCritical = 1000
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string strServiceShortName;
        public RM_APP_TYPE ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, StringBuilder strSessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint pSessionHandle, uint nFiles, string[] rgsFilenames, uint nApplications, IntPtr rgApplications, uint nServices, string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded, ref uint pnProcInfo, [In, Out] RM_PROCESS_INFO[]? rgAffectedApps, ref uint lpdwRebootReasons);
}
