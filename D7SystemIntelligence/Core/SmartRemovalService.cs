using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace D7SystemIntelligence.Core;

public enum RemovalVerdict { Blocked, HighRisk, Review, Safe }

public sealed record InstalledAppRecord(
    string RegistryId, string DisplayName, string DisplayVersion, string Publisher,
    string InstallLocation, string UninstallString, string QuietUninstallString,
    bool WindowsInstaller, bool SystemComponent);

public sealed record RemovalAnalysis(
    string TargetPath, string TargetType, RemovalVerdict Verdict, string VerdictText,
    IReadOnlyList<string> Reasons, IReadOnlyList<string> LockingProcesses, InstalledAppRecord? InstalledApp)
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
        "microsoft visual c++", ".net runtime", ".net host", "windows desktop runtime",
        "webview2 runtime", "windows update", "security update", "servicing stack",
        "amd chipset", "nvidia graphics driver", "realtek audio driver", "intel chipset",
        "microsoft edge webview2", "windows app runtime"
    };

    public SmartRemovalService()
    {
        _vaultRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "D7SystemIntelligence", "RestoreVault", "Removal");
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
            .Select(g => g.First())
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<RemovalAnalysis> AnalyzePathAsync(string input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input)) return Blocked(string.Empty, "غير معروف", "لم تدخل مسارًا.");

        string full;
        try { full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(input.Trim().Trim('"'))); }
        catch { return Blocked(input, "غير معروف", "المسار غير صالح."); }

        var isFile = File.Exists(full);
        var isDirectory = Directory.Exists(full);
        if (!isFile && !isDirectory) return Blocked(full, "غير موجود", "الملف أو المجلد غير موجود.");

        var type = isDirectory ? "مجلد" : "ملف";
        if (IsDriveRoot(full)) return Blocked(full, type, "D7 يمنع حذف جذر أي قرص.");
        if (IsProtectedShellRoot(full)) return Blocked(full, type, "D7 يمنع حذف مجلد مستخدم أساسي نفسه مثل Desktop/Documents/Downloads/Profile.");
        if (IsProtectedSystemPath(full)) return Blocked(full, type, "المسار داخل منطقة Windows/WindowsApps/ProgramData Microsoft المحمية.");
        if (IsD7Path(full)) return Blocked(full, type, "D7 يمنع حذف نفسه أو Restore Vault/Quarantine.");
        if (isFile && IsWrpProtected(full)) return Blocked(full, type, "Windows Resource Protection صنّف الملف كملف نظام محمي.");

        var reasons = new List<string>();
        var verdict = RemovalVerdict.Safe;
        var info = isDirectory ? (FileSystemInfo)new DirectoryInfo(full) : new FileInfo(full);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            verdict = RemovalVerdict.HighRisk;
            reasons.Add("الهدف Reparse Point/Junction/Symbolic Link؛ لا يسمح D7 بحذفه تلقائيًا.");
        }

        var app = FindOwningApp(full);
        if (app != null)
        {
            reasons.Add($"مرتبط ببرنامج مثبت: {app.DisplayName} {app.DisplayVersion}".Trim());
            if (IsProtectedDependency(app)) return Blocked(full, type, "الهدف تابع لتعريف/Runtime/مكوّن مشترك حساس.");
        }

        if (IsUnder(full, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)) ||
            IsUnder(full, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)) ||
            IsUnder(full, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)))
        {
            if (verdict == RemovalVerdict.Safe) verdict = RemovalVerdict.Review;
            reasons.Add("الهدف داخل Program Files/ProgramData؛ يحتاج مراجعة قبل الإزالة.");
        }
        else reasons.Add("الهدف خارج مناطق النظام المحمية.");

        var locks = await GetLockingProcessesAsync(full, isDirectory, cancellationToken);
        if (locks.Count > 0)
        {
            if (verdict == RemovalVerdict.Safe) verdict = RemovalVerdict.Review;
            reasons.Add("الهدف مستخدم حاليًا؛ D7 لن يجبر الحذف وهو مفتوح.");
        }

        return new RemovalAnalysis(full, type, verdict, VerdictArabic(verdict), reasons, locks, app);
    }

    public async Task<RemovalAnalysis> AnalyzeInstalledAppAsync(InstalledAppRecord app, CancellationToken cancellationToken = default)
    {
        if (IsProtectedDependency(app))
            return new RemovalAnalysis(app.InstallLocation, "برنامج", RemovalVerdict.Blocked,
                VerdictArabic(RemovalVerdict.Blocked),
                new[] { "D7 صنف هذا البرنامج كتعريف/Runtime/مكوّن مشترك حساس." },
                Array.Empty<string>(), app);

        var reasons = new List<string> { "سيتم تشغيل Uninstaller/Windows Installer أولًا بدل حذف الملفات بالقوة." };
        if (!string.IsNullOrWhiteSpace(app.InstallLocation)) reasons.Add("بعد نجاح الإزالة يمكن فحص InstallLocation كبقايا.");
        var hasUninstaller = !string.IsNullOrWhiteSpace(app.UninstallString) || !string.IsNullOrWhiteSpace(app.QuietUninstallString);
        if (!hasUninstaller) reasons.Add("لا يوجد UninstallString واضح؛ D7 لن يخترع أمر إزالة.");

        IReadOnlyList<string> locks = Array.Empty<string>();
        if (!string.IsNullOrWhiteSpace(app.InstallLocation) && Directory.Exists(app.InstallLocation))
            locks = await GetLockingProcessesAsync(app.InstallLocation, true, cancellationToken);

        var verdict = hasUninstaller ? RemovalVerdict.Safe : RemovalVerdict.Review;
        return new RemovalAnalysis(app.InstallLocation, "برنامج", verdict, VerdictArabic(verdict), reasons, locks, app);
    }

    public async Task<string> UninstallAppAsync(InstalledAppRecord app, bool deepCleanup, CancellationToken cancellationToken = default)
    {
        if (IsProtectedDependency(app)) return "D7 رفض الإزالة: البرنامج Dependency/Driver/System Component حساس.";
        var command = BuildUninstallCommand(app);
        if (command == null) return "لا يوجد UninstallString صالح؛ D7 لن يحذف مجلد البرنامج وكأنه Uninstaller.";

        int exitCode;
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = command.Value.File,
                Arguments = command.Value.Arguments,
                UseShellExecute = true,
                WorkingDirectory = !string.IsNullOrWhiteSpace(app.InstallLocation) && Directory.Exists(app.InstallLocation)
                    ? app.InstallLocation : Environment.GetFolderPath(Environment.SpecialFolder.System)
            });
            if (p == null) return "تعذر تشغيل أداة الإزالة.";
            await p.WaitForExitAsync(cancellationToken);
            exitCode = p.ExitCode;
        }
        catch (Exception ex) { return "فشل تشغيل Uninstaller: " + ex.Message; }

        if (exitCode is not (0 or 1605 or 1641 or 3010))
            return $"أداة الإزالة انتهت برمز {exitCode}. D7 لم يحذف البقايا بالقوة.";

        await Task.Delay(1000, cancellationToken);
        var notes = new List<string> { $"تم تشغيل إزالة {app.DisplayName} بنجاح." };
        if (string.IsNullOrWhiteSpace(app.InstallLocation)) return string.Join(Environment.NewLine, notes);

        foreach (var entry in _startup.Scan().Where(x => x.Enabled && CommandPointsInto(x.Command, app.InstallLocation)).ToArray())
        {
            try { notes.Add(_startup.Disable(entry.Id)); } catch { }
        }

        if (Directory.Exists(app.InstallLocation))
        {
            if (deepCleanup && IsExclusiveInstallDirectory(app))
            {
                var analysis = await AnalyzePathAsync(app.InstallLocation, cancellationToken);
                if (analysis.CanRemove && analysis.LockingProcesses.Count == 0 && analysis.Verdict != RemovalVerdict.HighRisk)
                    notes.Add(await QuarantineAsync(analysis, cancellationToken));
                else notes.Add("وجد D7 بقايا لكنه تركها لأنها مقفلة/عالية الخطورة/تحتاج مراجعة.");
            }
            else notes.Add("ما زال InstallLocation موجودًا؛ لم يحذف D7 مجلدًا غير مثبت أنه حصري وآمن.");
        }
        return string.Join(Environment.NewLine, notes);
    }

    public Task<string> QuarantineAsync(RemovalAnalysis analysis, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!analysis.CanRemove) return Task.FromResult("D7 رفض الإزالة: الهدف محمي.");
        if (analysis.LockingProcesses.Count > 0) return Task.FromResult("الهدف مستخدم الآن بواسطة: " + string.Join("، ", analysis.LockingProcesses));
        if (analysis.Verdict == RemovalVerdict.HighRisk) return Task.FromResult("Reparse Point/Junction لا ينتقل إلى Quarantine تلقائيًا.");

        var full = analysis.TargetPath;
        if (!File.Exists(full) && !Directory.Exists(full)) return Task.FromResult("الهدف لم يعد موجودًا.");
        var driveRoot = Path.GetPathRoot(full);
        if (string.IsNullOrWhiteSpace(driveRoot)) return Task.FromResult("تعذر تحديد القرص.");

        var quarantineRoot = Path.Combine(driveRoot, ".D7Quarantine");
        Directory.CreateDirectory(quarantineRoot);
        try { File.SetAttributes(quarantineRoot, File.GetAttributes(quarantineRoot) | FileAttributes.Hidden); } catch { }

        var trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        var destination = Path.Combine(quarantineRoot,
            DateTime.Now.ToString("yyyyMMdd-HHmmss") + "_" + Guid.NewGuid().ToString("N") + "_" + Sanitize(name));

        try
        {
            if (File.Exists(full)) File.Move(full, destination);
            else Directory.Move(full, destination);
        }
        catch (Exception ex) { return Task.FromResult("تعذر نقل الهدف إلى Quarantine: " + ex.Message); }

        var record = new QuarantineRecord
        {
            Id = Guid.NewGuid().ToString("N"), OriginalPath = full, QuarantinedPath = destination,
            TargetType = analysis.TargetType, CreatedUtc = DateTime.UtcNow
        };
        var records = LoadManifest();
        records.Add(record);
        SaveManifest(records);
        return Task.FromResult($"تم نقل الهدف إلى D7 Quarantine مع إمكانية الاستعادة.\nID: {record.Id}");
    }

    public Task<string> PermanentDeleteAsync(RemovalAnalysis analysis, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (analysis.Verdict != RemovalVerdict.Safe) return Task.FromResult("الحذف النهائي مسموح فقط للأهداف المصنفة Safe.");
        if (analysis.LockingProcesses.Count > 0) return Task.FromResult("الهدف مستخدم بواسطة: " + string.Join("، ", analysis.LockingProcesses));
        try
        {
            if (File.Exists(analysis.TargetPath)) File.Delete(analysis.TargetPath);
            else if (Directory.Exists(analysis.TargetPath)) Directory.Delete(analysis.TargetPath, true);
            else return Task.FromResult("الهدف غير موجود.");
            return Task.FromResult("تم الحذف النهائي للهدف المصنف Safe.");
        }
        catch (Exception ex) { return Task.FromResult("فشل الحذف النهائي: " + ex.Message); }
    }

    public IReadOnlyList<QuarantineRecord> ListQuarantine()
        => LoadManifest().OrderByDescending(x => x.CreatedUtc).ToArray();

    public string RestoreQuarantine(string id)
    {
        var records = LoadManifest();
        var record = records.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (record == null) return "عنصر Quarantine غير موجود.";
        if (!File.Exists(record.QuarantinedPath) && !Directory.Exists(record.QuarantinedPath)) return "بيانات Quarantine غير موجودة.";
        if (File.Exists(record.OriginalPath) || Directory.Exists(record.OriginalPath)) return "المسار الأصلي مستخدم؛ D7 لن يستبدله.";
        try
        {
            var parent = Path.GetDirectoryName(record.OriginalPath);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
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
        => ScanInstalledApps()
            .Where(x => !string.IsNullOrWhiteSpace(x.InstallLocation) && IsUnder(path, x.InstallLocation))
            .OrderByDescending(x => x.InstallLocation.Length)
            .FirstOrDefault();

    private bool IsExclusiveInstallDirectory(InstalledAppRecord app)
    {
        if (string.IsNullOrWhiteSpace(app.InstallLocation) || !Directory.Exists(app.InstallLocation)) return false;
        if (ScanInstalledApps().Count(x => !string.IsNullOrWhiteSpace(x.InstallLocation) && PathEquals(x.InstallLocation, app.InstallLocation)) > 1) return false;
        var dir = Normalize(Path.GetFileName(app.InstallLocation.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        var name = Normalize(app.DisplayName);
        return dir.Length >= 4 && (name.Contains(dir, StringComparison.OrdinalIgnoreCase) || dir.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    private static (string File, string Arguments)? BuildUninstallCommand(InstalledAppRecord app)
    {
        var raw = !string.IsNullOrWhiteSpace(app.QuietUninstallString) ? app.QuietUninstallString : app.UninstallString;
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var guid = Regex.Match(raw, @"\{[0-9A-Fa-f-]{36}\}");
        if (app.WindowsInstaller || raw.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
            return guid.Success ? ("msiexec.exe", $"/x {guid.Value} /passive /norestart") : null;

        raw = Environment.ExpandEnvironmentVariables(raw.Trim());
        if (raw.Length > 1 && raw[0] == '"')
        {
            var end = raw.IndexOf('"', 1);
            if (end <= 1) return null;
            return (raw.Substring(1, end - 1), raw[(end + 1)..].Trim());
        }

        if (File.Exists(raw)) return (raw, string.Empty);
        var exeIndex = raw.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex >= 0)
        {
            var file = raw[..(exeIndex + 4)].Trim();
            var args = raw[(exeIndex + 4)..].Trim();
            return (file, args);
        }
        return null;
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
                    list.Add(new InstalledAppRecord(
                        prefix + "|" + subName, display,
                        key.GetValue("DisplayVersion")?.ToString() ?? string.Empty,
                        key.GetValue("Publisher")?.ToString() ?? string.Empty,
                        Environment.ExpandEnvironmentVariables(key.GetValue("InstallLocation")?.ToString() ?? string.Empty).Trim().Trim('"'),
                        key.GetValue("UninstallString")?.ToString() ?? string.Empty,
                        key.GetValue("QuietUninstallString")?.ToString() ?? string.Empty,
                        Convert.ToInt32(key.GetValue("WindowsInstaller", 0)) == 1,
                        Convert.ToInt32(key.GetValue("SystemComponent", 0)) == 1));
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
           command.Contains(installLocation.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

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
        try
        {
            var left = Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var right = Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return left.Equals(right, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static string Normalize(string text) => Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9]+", string.Empty);
    private static string Sanitize(string name) => string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

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
    private static extern bool SfcIsFileProtected(IntPtr rpcHandle, string protectedFileName);
}

internal static class RestartManagerInspector
{
    private const int ErrorMoreData = 234;
    private const int SessionKeyLength = 32;

    public static IReadOnlyList<string> GetLockingProcessNames(string filePath)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var key = new StringBuilder(SessionKeyLength + 1);
        if (RmStartSession(out var handle, 0, key) != 0) return result.ToArray();
        try
        {
            var resources = new[] { filePath };
            if (RmRegisterResources(handle, 1, resources, 0, IntPtr.Zero, 0, null) != 0) return result.ToArray();
            uint needed = 0, count = 0, reasons = 0;
            var code = RmGetList(handle, out needed, ref count, null, ref reasons);
            if (code != ErrorMoreData || needed == 0) return result.ToArray();
            var apps = new RM_PROCESS_INFO[needed];
            count = needed;
            code = RmGetList(handle, out needed, ref count, apps, ref reasons);
            if (code != 0) return result.ToArray();
            for (var i = 0; i < count; i++)
            {
                if (!string.IsNullOrWhiteSpace(apps[i].strAppName)) result.Add(apps[i].strAppName);
                else if (apps[i].Process.dwProcessId > 0)
                {
                    try { using var p = Process.GetProcessById(apps[i].Process.dwProcessId); result.Add(p.ProcessName); } catch { }
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
        RmUnknownApp = 0, RmMainWindow = 1, RmOtherWindow = 2, RmService = 3,
        RmExplorer = 4, RmConsole = 5, RmCritical = 1000
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
    private static extern int RmStartSession(out uint sessionHandle, int sessionFlags, StringBuilder sessionKey);
    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint sessionHandle);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint sessionHandle, uint fileCount, string[] fileNames,
        uint applicationCount, IntPtr applications, uint serviceCount, string[]? serviceNames);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmGetList(uint sessionHandle, out uint procInfoNeeded, ref uint procInfo,
        [In, Out] RM_PROCESS_INFO[]? affectedApps, ref uint rebootReasons);
}
