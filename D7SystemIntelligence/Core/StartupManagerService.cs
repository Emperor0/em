using Microsoft.Win32;
using System.Text.Json;

namespace D7SystemIntelligence.Core;

public sealed record StartupEntry(
    string Id,
    string Name,
    string Command,
    string Scope,
    string Source,
    bool Enabled,
    string ImpactHint)
{
    public string StateText => Enabled ? "شغال" : "طافي";
}

internal sealed class StartupBackupRecord
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string RegistryHive { get; set; } = string.Empty;
    public string RegistryView { get; set; } = string.Empty;
    public string RegistryPath { get; set; } = string.Empty;
    public int RegistryValueKind { get; set; }
    public string OriginalFilePath { get; set; } = string.Empty;
    public string DisabledFilePath { get; set; } = string.Empty;
}

public sealed class StartupManagerService
{
    private readonly string _root;
    private readonly string _backupPath;
    private readonly string _disabledFolder;

    public StartupManagerService()
    {
        _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D7SystemIntelligence", "RestoreVault", "Startup");
        _disabledFolder = Path.Combine(_root, "DisabledFiles");
        Directory.CreateDirectory(_disabledFolder);
        _backupPath = Path.Combine(_root, "startup-backup.json");
    }

    public IReadOnlyList<StartupEntry> Scan()
    {
        var list = new List<StartupEntry>();
        ScanRegistry(list, RegistryHive.CurrentUser, RegistryView.Default, @"Software\Microsoft\Windows\CurrentVersion\Run", "المستخدم", "HKCU Run", "Run");
        ScanRegistry(list, RegistryHive.LocalMachine, RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Run", "الجهاز", "HKLM Run 64", "Run");
        ScanRegistry(list, RegistryHive.LocalMachine, RegistryView.Registry32, @"Software\Microsoft\Windows\CurrentVersion\Run", "الجهاز", "HKLM Run 32", "Run32");
        ScanFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "المستخدم", "Startup Folder", RegistryHive.CurrentUser);
        ScanFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "الجهاز", "Common Startup Folder", RegistryHive.LocalMachine);

        var disabled = LoadBackups();
        foreach (var b in disabled.Values)
        {
            if (list.Any(x => x.Id.Equals(b.Id, StringComparison.OrdinalIgnoreCase))) continue;
            list.Add(new StartupEntry(b.Id, b.Name, b.Command, b.Scope, b.Source, false, ImpactHint(b.Name, b.Command)));
        }

        return list.OrderByDescending(x => x.Enabled).ThenBy(x => x.Scope).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public string Disable(string id)
    {
        var entry = Scan().FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return "عنصر Startup غير موجود.";
        if (!entry.Enabled) return "العنصر طافي بالفعل.";

        if (TrySetWindowsStartupState(entry, false, out var stateMessage))
            return stateMessage;

        // Fallback for systems where StartupApproved cannot be changed. This preserves the previous D7 reversible method.
        var backups = LoadBackups();
        if (entry.Id.StartsWith("REG|", StringComparison.OrdinalIgnoreCase))
        {
            var parts = entry.Id.Split('|');
            if (parts.Length < 5) return "معرف Registry غير صالح.";
            var hive = parts[1].Equals("HKCU", StringComparison.OrdinalIgnoreCase) ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
            var view = parts[2] switch { "64" => RegistryView.Registry64, "32" => RegistryView.Registry32, _ => RegistryView.Default };
            var path = parts[3];
            var valueName = parts[4];
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(path, writable: true);
            if (key == null) return "تعذر فتح Run key للكتابة.";
            var value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString();
            if (value == null) return "قيمة Startup لم تعد موجودة.";
            var kind = key.GetValueKind(valueName);
            backups[entry.Id] = new StartupBackupRecord
            {
                Id = entry.Id,
                Name = entry.Name,
                Command = value,
                Scope = entry.Scope,
                Source = entry.Source,
                RegistryHive = hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM",
                RegistryView = view == RegistryView.Registry64 ? "64" : view == RegistryView.Registry32 ? "32" : "Default",
                RegistryPath = path,
                RegistryValueKind = (int)kind
            };
            SaveBackups(backups);
            key.DeleteValue(valueName, throwOnMissingValue: false);
            return $"تم تعطيل {entry.Name} وحفظ قيمته الأصلية في Restore Vault لأن Windows StartupApproved لم يكن متاحًا.";
        }

        if (entry.Id.StartsWith("FILE|", StringComparison.OrdinalIgnoreCase))
        {
            var original = entry.Command;
            if (!File.Exists(original)) return "ملف Startup لم يعد موجودًا.";
            var safeName = Guid.NewGuid().ToString("N") + Path.GetExtension(original);
            var disabledPath = Path.Combine(_disabledFolder, safeName);
            File.Move(original, disabledPath);
            backups[entry.Id] = new StartupBackupRecord
            {
                Id = entry.Id,
                Name = entry.Name,
                Command = original,
                Scope = entry.Scope,
                Source = entry.Source,
                OriginalFilePath = original,
                DisabledFilePath = disabledPath
            };
            SaveBackups(backups);
            return $"تم نقل {entry.Name} خارج Startup Folder مع حفظ مكانه الأصلي.";
        }

        return "نوع Startup غير مدعوم.";
    }

    public string Restore(string id)
    {
        var backups = LoadBackups();
        if (backups.TryGetValue(id, out var b))
        {
            if (id.StartsWith("REG|", StringComparison.OrdinalIgnoreCase))
            {
                var hive = b.RegistryHive == "HKCU" ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
                var view = b.RegistryView switch { "64" => RegistryView.Registry64, "32" => RegistryView.Registry32, _ => RegistryView.Default };
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(b.RegistryPath, writable: true);
                if (key == null) return "تعذر فتح Run key للاستعادة.";
                var valueName = id.Split('|').Last();
                var kind = Enum.IsDefined(typeof(RegistryValueKind), b.RegistryValueKind) ? (RegistryValueKind)b.RegistryValueKind : RegistryValueKind.String;
                key.SetValue(valueName, b.Command, kind);
                backups.Remove(id);
                SaveBackups(backups);
                return $"تمت استعادة {b.Name} إلى Startup Registry.";
            }

            if (id.StartsWith("FILE|", StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(b.DisabledFilePath)) return "نسخة ملف Startup المعطلة غير موجودة.";
                Directory.CreateDirectory(Path.GetDirectoryName(b.OriginalFilePath)!);
                if (File.Exists(b.OriginalFilePath)) return "يوجد ملف بنفس الاسم في Startup؛ D7 لن يستبدله تلقائيًا.";
                File.Move(b.DisabledFilePath, b.OriginalFilePath);
                backups.Remove(id);
                SaveBackups(backups);
                return $"تمت استعادة {b.Name} إلى Startup Folder.";
            }
        }

        var entry = Scan().FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return "عنصر Startup غير موجود ولا توجد له نسخة Restore.";
        if (entry.Enabled) return "العنصر شغال بالفعل.";
        return TrySetWindowsStartupState(entry, true, out var message)
            ? message
            : "تعذر تشغيل العنصر عبر Windows StartupApproved. لم يغيّر D7 أي شيء.";
    }

    private static void ScanRegistry(List<StartupEntry> list, RegistryHive hive, RegistryView view, string path, string scope, string source, string approvalSubKey)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(path, writable: false);
            if (key == null) return;
            foreach (var name in key.GetValueNames())
            {
                var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? string.Empty;
                var id = $"REG|{(hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM")}|{(view == RegistryView.Registry64 ? "64" : view == RegistryView.Registry32 ? "32" : "D")}|{path}|{name}";
                var enabled = ReadWindowsStartupState(hive, view, approvalSubKey, name);
                list.Add(new StartupEntry(id, name, value, scope, source, enabled, ImpactHint(name, value)));
            }
        }
        catch { }
    }

    private static void ScanFolder(List<StartupEntry> list, string folder, string scope, string source, RegistryHive hive)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var id = $"FILE|{file}";
                var name = Path.GetFileNameWithoutExtension(file);
                var valueName = Path.GetFileName(file);
                var enabled = ReadWindowsStartupState(hive, RegistryView.Default, "StartupFolder", valueName);
                list.Add(new StartupEntry(id, name, file, scope, source, enabled, ImpactHint(Path.GetFileName(file), file)));
            }
        }
        catch { }
    }

    private static bool ReadWindowsStartupState(RegistryHive hive, RegistryView view, string subKey, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\{subKey}", writable: false);
            if (key?.GetValue(valueName) is not byte[] bytes || bytes.Length == 0) return true;
            return bytes[0] switch
            {
                1 or 3 or 5 or 7 or 9 => false,
                2 or 4 or 6 or 8 => true,
                _ => true
            };
        }
        catch { return true; }
    }

    private static bool TrySetWindowsStartupState(StartupEntry entry, bool enabled, out string message)
    {
        message = string.Empty;
        try
        {
            RegistryHive hive;
            RegistryView view;
            string subKey;
            string valueName;

            if (entry.Id.StartsWith("REG|", StringComparison.OrdinalIgnoreCase))
            {
                var parts = entry.Id.Split('|');
                if (parts.Length < 5) return false;
                hive = parts[1].Equals("HKCU", StringComparison.OrdinalIgnoreCase) ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
                view = parts[2] switch { "64" => RegistryView.Registry64, "32" => RegistryView.Registry32, _ => RegistryView.Default };
                subKey = parts[2] == "32" ? "Run32" : "Run";
                valueName = parts[4];
            }
            else if (entry.Id.StartsWith("FILE|", StringComparison.OrdinalIgnoreCase))
            {
                hive = entry.Scope == "الجهاز" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
                view = RegistryView.Default;
                subKey = "StartupFolder";
                valueName = Path.GetFileName(entry.Command);
            }
            else return false;

            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.CreateSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\{subKey}", writable: true);
            if (key == null) return false;
            key.SetValue(valueName, enabled ? EnabledApprovalBytes() : DisabledApprovalBytes(), RegistryValueKind.Binary);
            message = enabled ? $"تم تشغيل {entry.Name} في Startup." : $"تم إطفاء {entry.Name} من Startup بدون حذف أمر التشغيل الأصلي.";
            return true;
        }
        catch (Exception ex)
        {
            message = "StartupApproved غير متاح: " + ex.Message;
            return false;
        }
    }

    private static byte[] EnabledApprovalBytes() => new byte[12] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

    private static byte[] DisabledApprovalBytes()
    {
        var result = new byte[12] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        var fileTime = BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc());
        Buffer.BlockCopy(fileTime, 0, result, 4, 8);
        return result;
    }

    private static string ImpactHint(string name, string command)
    {
        var text = (name + " " + command).ToLowerInvariant();
        if (text.Contains("security") || text.Contains("defender") || text.Contains("antivirus") || text.Contains("audio") || text.Contains("driver") || text.Contains("anticheat")) return "حساس/قد يكون ضروريًا";
        if (text.Contains("steam") || text.Contains("epic") || text.Contains("discord") || text.Contains("onedrive") || text.Contains("adobe") || text.Contains("teams")) return "قابل للتأجيل عادة";
        return "راجع قبل التعطيل";
    }

    private Dictionary<string, StartupBackupRecord> LoadBackups()
    {
        try
        {
            if (!File.Exists(_backupPath)) return new(StringComparer.OrdinalIgnoreCase);
            var list = JsonSerializer.Deserialize<List<StartupBackupRecord>>(File.ReadAllText(_backupPath)) ?? [];
            return list.ToDictionary(x => x.Id, x => x, StringComparer.OrdinalIgnoreCase);
        }
        catch { return new(StringComparer.OrdinalIgnoreCase); }
    }

    private void SaveBackups(Dictionary<string, StartupBackupRecord> backups)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(_backupPath, JsonSerializer.Serialize(backups.Values.OrderBy(x => x.Name).ToArray(), new JsonSerializerOptions { WriteIndented = true }));
    }
}
