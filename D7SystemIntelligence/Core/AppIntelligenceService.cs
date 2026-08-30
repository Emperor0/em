using Microsoft.Win32;
using System.Diagnostics;
using System.Text.Json;

namespace D7SystemIntelligence.Core;

public enum ManagedAppId
{
    Discord,
    Steam,
    NvidiaApp,
    Obs,
    TikTokLiveStudio,
    Chrome,
    Edge
}

public enum AppProfileMode
{
    Normal,
    Gaming,
    Streaming
}

public sealed record AppCapability(string Name, bool Supported, string Detail);

public sealed record ManagedAppState(
    ManagedAppId Id,
    string DisplayName,
    bool Installed,
    bool Running,
    string? ExecutablePath,
    IReadOnlyList<string> RunningProcesses,
    IReadOnlyList<AppCapability> Capabilities,
    string SafetyNote);

internal sealed class AppRunEntryBackup
{
    public string Hive { get; set; } = string.Empty;
    public string ValueName { get; set; } = string.Empty;
    public string ValueData { get; set; } = string.Empty;
}

internal sealed class AppProfileBackup
{
    public AppProfileMode Mode { get; set; }
    public Dictionary<int, string> Priorities { get; set; } = [];
    public List<AppRunEntryBackup> StartupEntries { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class AppIntelligenceService
{
    private readonly string _vault;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public AppIntelligenceService()
    {
        _vault = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D7SystemIntelligence", "RestoreVault", "Apps");
        Directory.CreateDirectory(_vault);
    }

    public Task<IReadOnlyList<ManagedAppState>> ScanAsync(CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<ManagedAppState>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Definitions.Select(Inspect).ToArray();
        }, cancellationToken);

    public async Task<string> ApplyProfileAsync(ManagedAppId id, AppProfileMode mode, CancellationToken cancellationToken = default)
    {
        var definition = Definitions.First(x => x.Id == id);
        var state = Inspect(definition);
        if (!state.Installed && !state.Running) return $"{state.DisplayName}: غير مثبت/غير مكتشف.";

        await RestoreProfileAsync(id, silentWhenMissing: true, cancellationToken);
        var backup = new AppProfileBackup { Mode = mode };
        var messages = new List<string>();

        foreach (var rule in definition.ProcessRules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var process in Processes(rule.ProcessName))
            {
                using (process)
                {
                    try
                    {
                        var current = process.PriorityClass;
                        var target = rule.Priority(mode);
                        if (!target.HasValue)
                        {
                            messages.Add($"{process.ProcessName}: لا تغيير في {mode}.");
                            continue;
                        }

                        if (current == target.Value)
                        {
                            messages.Add($"{process.ProcessName}: Already optimal ({current}).");
                            continue;
                        }

                        backup.Priorities[process.Id] = current.ToString();
                        process.PriorityClass = target.Value;
                        var verified = process.PriorityClass;
                        messages.Add(verified == target.Value
                            ? $"{process.ProcessName}: {current} → {target.Value} [Verified]"
                            : $"{process.ProcessName}: Windows لم يثبت Priority المطلوبة.");
                    }
                    catch (Exception ex)
                    {
                        messages.Add($"{rule.ProcessName}: تخطي — {ex.Message}");
                    }
                }
            }
        }

        await SaveBackupAsync(id, backup, cancellationToken);
        if (messages.Count == 0) messages.Add("لا توجد عملية شغالة تحتاج تعديل الآن. تم حفظ Profile فقط.");
        return $"{state.DisplayName} • {mode}\n" + string.Join(Environment.NewLine, messages);
    }

    public async Task<string> RestoreProfileAsync(ManagedAppId id, bool silentWhenMissing = false, CancellationToken cancellationToken = default)
    {
        var path = BackupPath(id);
        if (!File.Exists(path)) return silentWhenMissing ? string.Empty : "لا توجد تغييرات App Profile محفوظة للاستعادة.";

        AppProfileBackup? backup;
        try { backup = JsonSerializer.Deserialize<AppProfileBackup>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions); }
        catch (Exception ex) { return "تعذر قراءة App Restore: " + ex.Message; }
        if (backup == null) return "App Restore فارغ.";

        var restored = 0;
        foreach (var pair in backup.Priorities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var process = Process.GetProcessById(pair.Key);
                if (!process.HasExited && Enum.TryParse<ProcessPriorityClass>(pair.Value, out var priority))
                {
                    process.PriorityClass = priority;
                    if (process.PriorityClass == priority) restored++;
                }
            }
            catch { }
        }

        try { File.Delete(path); } catch { }
        return $"تمت استعادة Priority لـ{restored} عملية تخص {DisplayName(id)}. العمليات التي انتهت لا تحتاج استعادة.";
    }

    public async Task<string> DisableStartupAsync(ManagedAppId id, CancellationToken cancellationToken = default)
    {
        var definition = Definitions.First(x => x.Id == id);
        var matches = FindStartupEntries(definition).ToList();
        if (matches.Count == 0) return $"{definition.DisplayName}: لم يجد D7KT Run entry آمنًا لتعطيله. لم يتم لمس Scheduled Tasks أو Services.";

        var backupPath = StartupBackupPath(id);
        await File.WriteAllTextAsync(backupPath, JsonSerializer.Serialize(matches, JsonOptions), cancellationToken);
        var disabled = 0;
        foreach (var item in matches)
        {
            try
            {
                using var key = OpenRunKey(item.Hive, writable: true);
                if (key?.GetValue(item.ValueName) != null)
                {
                    key.DeleteValue(item.ValueName, false);
                    disabled++;
                }
            }
            catch { }
        }
        return $"{definition.DisplayName}: تم تعطيل {disabled} Run entry فقط. لم يتم تعطيل Service/Updater أو حذف أي ملف.";
    }

    public async Task<string> RestoreStartupAsync(ManagedAppId id, CancellationToken cancellationToken = default)
    {
        var path = StartupBackupPath(id);
        if (!File.Exists(path)) return "لا توجد Startup backup لهذا البرنامج.";
        List<AppRunEntryBackup>? entries;
        try { entries = JsonSerializer.Deserialize<List<AppRunEntryBackup>>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions); }
        catch (Exception ex) { return "تعذر قراءة Startup backup: " + ex.Message; }
        if (entries == null) return "Startup backup فارغ.";

        var restored = 0;
        foreach (var item in entries)
        {
            try
            {
                using var key = OpenRunKey(item.Hive, writable: true);
                if (key == null) continue;
                key.SetValue(item.ValueName, item.ValueData, RegistryValueKind.String);
                restored++;
            }
            catch { }
        }
        if (restored == entries.Count) try { File.Delete(path); } catch { }
        return $"تمت استعادة {restored}/{entries.Count} Startup entries.";
    }

    public async Task<string> CleanSafeCacheAsync(ManagedAppId id, CancellationToken cancellationToken = default)
    {
        var definition = Definitions.First(x => x.Id == id);
        if (definition.SafeCacheRoots.Count == 0)
            return $"{definition.DisplayName}: لا يوجد Cache path اعتمده D7KT كآمن للحذف. لا يوجد زر تنظيف وهمي.";

        long bytes = 0;
        var files = 0;
        var skipped = 0;
        foreach (var root in definition.SafeCacheRoots.Select(Expand).Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            IEnumerable<string> candidates;
            try { candidates = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToArray(); }
            catch { continue; }
            foreach (var file in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(file);
                    var size = info.Length;
                    info.Delete();
                    bytes += size;
                    files++;
                }
                catch { skipped++; }
            }
        }
        return $"{definition.DisplayName}: حذف {files} ملف UI/cache آمن • {FormatBytes(bytes)} • تخطى {skipped} ملف مستخدم. لا يتم حذف shader cache أو بيانات تسجيل الدخول.";
    }

    public string OpenApp(ManagedAppId id)
    {
        var definition = Definitions.First(x => x.Id == id);
        var state = Inspect(definition);
        if (id == ManagedAppId.Steam)
        {
            try
            {
                Process.Start(new ProcessStartInfo("steam://open/settings") { UseShellExecute = true });
                return "تم فتح Steam Settings.";
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(state.ExecutablePath) || !File.Exists(state.ExecutablePath))
            return $"تعذر تحديد الملف التنفيذي لـ{state.DisplayName}.";
        try
        {
            Process.Start(new ProcessStartInfo(state.ExecutablePath) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(state.ExecutablePath) ?? string.Empty });
            return $"تم فتح {state.DisplayName}.";
        }
        catch (Exception ex) { return $"تعذر فتح {state.DisplayName}: {ex.Message}"; }
    }

    public async Task<string> StopUserInterfaceAsync(ManagedAppId id, CancellationToken cancellationToken = default)
    {
        var definition = Definitions.First(x => x.Id == id);
        if (definition.SafeCloseProcesses.Count == 0)
            return $"{definition.DisplayName}: D7KT لا يملك قائمة Process آمنة للإغلاق لهذا البرنامج.";

        var closed = 0;
        var names = new List<string>();
        foreach (var processName in definition.SafeCloseProcesses)
        {
            foreach (var process in Processes(processName))
            {
                using (process)
                {
                    try
                    {
                        if (process.MainWindowHandle != IntPtr.Zero)
                        {
                            process.CloseMainWindow();
                            try { await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(2), cancellationToken); } catch { }
                        }
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: false);
                            await process.WaitForExitAsync(cancellationToken);
                        }
                        closed++;
                        names.Add(processName);
                    }
                    catch { }
                }
            }
        }
        return closed == 0
            ? $"{definition.DisplayName}: لا توجد UI process قابلة للإغلاق الآن."
            : $"تم إغلاق {closed} عملية UI آمنة: {string.Join("، ", names.Distinct(StringComparer.OrdinalIgnoreCase))}.";
    }

    private ManagedAppState Inspect(AppDefinition definition)
    {
        var running = definition.ProcessRules.SelectMany(x => Processes(x.ProcessName).Select(p =>
        {
            try { return p.ProcessName; }
            finally { p.Dispose(); }
        })).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var exe = FindExecutable(definition);
        var installed = exe != null || definition.InstallMarkers.Any(x => File.Exists(Expand(x)) || Directory.Exists(Expand(x)));
        var startup = FindStartupEntries(definition).Any();
        var caches = definition.SafeCacheRoots.Select(Expand).Any(Directory.Exists);
        var capabilities = new List<AppCapability>
        {
            new("Process Profile", definition.ProcessRules.Count > 0, "Priority profiles قابلة للقياس والرجوع؛ لا يغير CPU affinity عشوائيًا."),
            new("Startup", startup, startup ? "Run entry مكتشف ويمكن Backup/Disable/Restore." : "لا يوجد Run entry آمن مكتشف؛ Scheduled Tasks/Services لا تُلمس."),
            new("Safe Cache", caches, caches ? "UI/cache paths معتمدة فقط." : "لا يوجد cache path اعتمده D7KT لهذا التثبيت."),
            new("Native Settings", exe != null || definition.Id == ManagedAppId.Steam, "يفتح واجهة البرنامج الأصلية للإعدادات proprietary بدل Toggle مزيف."),
            new("Safe UI Close", definition.SafeCloseProcesses.Count > 0, definition.SafetyNote)
        };
        return new ManagedAppState(definition.Id, definition.DisplayName, installed, running.Length > 0, exe, running, capabilities, definition.SafetyNote);
    }

    private static string? FindExecutable(AppDefinition definition)
    {
        foreach (var marker in definition.ExecutableCandidates.Select(Expand))
        {
            if (marker.Contains('*'))
            {
                var root = Path.GetDirectoryName(marker[..marker.IndexOf('*')]);
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
                var name = Path.GetFileName(marker);
                var file = Directory.EnumerateFiles(root, name, SearchOption.AllDirectories).OrderByDescending(x => x).FirstOrDefault();
                if (file != null) return file;
            }
            else if (File.Exists(marker)) return marker;
        }

        foreach (var processName in definition.ProcessRules.Select(x => x.ProcessName))
        {
            foreach (var process in Processes(processName))
            {
                using (process)
                {
                    try { if (File.Exists(process.MainModule?.FileName)) return process.MainModule!.FileName; } catch { }
                }
            }
        }
        return null;
    }

    private static IEnumerable<AppRunEntryBackup> FindStartupEntries(AppDefinition definition)
    {
        foreach (var hive in new[] { "HKCU", "HKLM" })
        {
            using var key = OpenRunKey(hive, writable: false);
            if (key == null) continue;
            foreach (var name in key.GetValueNames())
            {
                var data = key.GetValue(name)?.ToString() ?? string.Empty;
                var combined = (name + " " + data).ToLowerInvariant();
                if (!definition.StartupTokens.Any(x => combined.Contains(x, StringComparison.OrdinalIgnoreCase))) continue;
                yield return new AppRunEntryBackup { Hive = hive, ValueName = name, ValueData = data };
            }
        }
    }

    private static RegistryKey? OpenRunKey(string hive, bool writable)
    {
        var root = hive == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;
        return root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable);
    }

    private async Task SaveBackupAsync(ManagedAppId id, AppProfileBackup backup, CancellationToken token)
        => await File.WriteAllTextAsync(BackupPath(id), JsonSerializer.Serialize(backup, JsonOptions), token);

    private string BackupPath(ManagedAppId id) => Path.Combine(_vault, $"{id}-profile.json");
    private string StartupBackupPath(ManagedAppId id) => Path.Combine(_vault, $"{id}-startup.json");

    private static IEnumerable<Process> Processes(string name)
    {
        try { return Process.GetProcessesByName(name); }
        catch { return []; }
    }

    private static string Expand(string value)
    {
        var result = Environment.ExpandEnvironmentVariables(value);
        result = result.Replace("%PROGRAMFILESX86%", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("%PROGRAMFILES%", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("%LOCALAPPDATA%", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("%APPDATA%", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), StringComparison.OrdinalIgnoreCase);
        return result;
    }

    private static string FormatBytes(long value)
    {
        if (value >= 1024L * 1024 * 1024) return $"{value / 1024d / 1024 / 1024:0.00} GB";
        if (value >= 1024L * 1024) return $"{value / 1024d / 1024:0.0} MB";
        if (value >= 1024L) return $"{value / 1024d:0.0} KB";
        return $"{value} B";
    }

    private static string DisplayName(ManagedAppId id) => Definitions.First(x => x.Id == id).DisplayName;

    private sealed record ProcessRule(string ProcessName, Func<AppProfileMode, ProcessPriorityClass?> Priority);
    private sealed record AppDefinition(
        ManagedAppId Id,
        string DisplayName,
        IReadOnlyList<string> ExecutableCandidates,
        IReadOnlyList<string> InstallMarkers,
        IReadOnlyList<string> StartupTokens,
        IReadOnlyList<string> SafeCacheRoots,
        IReadOnlyList<string> SafeCloseProcesses,
        IReadOnlyList<ProcessRule> ProcessRules,
        string SafetyNote);

    private static readonly IReadOnlyList<AppDefinition> Definitions =
    [
        new(
            ManagedAppId.Discord,
            "Discord",
            ["%LOCALAPPDATA%\\Discord\\Update.exe", "%LOCALAPPDATA%\\Discord\\app-*\\Discord.exe"],
            ["%LOCALAPPDATA%\\Discord"],
            ["discord"],
            ["%APPDATA%\\discord\\Cache", "%APPDATA%\\discord\\Code Cache", "%APPDATA%\\discord\\GPUCache"],
            ["Discord"],
            [new("Discord", mode => mode == AppProfileMode.Gaming ? ProcessPriorityClass.Normal : mode == AppProfileMode.Streaming ? ProcessPriorityClass.AboveNormal : ProcessPriorityClass.Normal)],
            "D7KT لا يخفض Discord في Gaming لأن الصوت/Voice أهم من توفير حمل بسيط. إغلاق Discord يحتاج ضغط المستخدم ولا يتم داخل Mission تلقائيًا."),

        new(
            ManagedAppId.Steam,
            "Steam",
            ["%PROGRAMFILESX86%\\Steam\\steam.exe", "%PROGRAMFILES%\\Steam\\steam.exe"],
            ["%PROGRAMFILESX86%\\Steam", "%PROGRAMFILES%\\Steam"],
            ["steam"],
            ["%LOCALAPPDATA%\\Steam\\htmlcache"],
            ["steamwebhelper"],
            [
                new("steam", _ => ProcessPriorityClass.Normal),
                new("steamwebhelper", mode => mode == AppProfileMode.Gaming ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal)
            ],
            "D7KT قد يخفض steamwebhelper فقط أثناء Gaming؛ لا يقتل Steam Client Service ولا يوقف تنزيلات بطريقة غير موثقة."),

        new(
            ManagedAppId.NvidiaApp,
            "NVIDIA App",
            ["%PROGRAMFILES%\\NVIDIA Corporation\\NVIDIA App\\CEF\\NVIDIA App.exe", "%PROGRAMFILES%\\NVIDIA Corporation\\NVIDIA App\\NVIDIA App.exe"],
            ["%PROGRAMFILES%\\NVIDIA Corporation\\NVIDIA App"],
            ["nvidia app"],
            [],
            ["NVIDIA App", "NVIDIA Overlay"],
            [
                new("NVIDIA App", mode => mode == AppProfileMode.Gaming ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal),
                new("NVIDIA Overlay", _ => ProcessPriorityClass.Normal)
            ],
            "لا يتم لمس nvcontainer أو Display Container أو خدمات التعريف الأساسية. Overlay يُغلق فقط بأمر يدوي صريح."),

        new(
            ManagedAppId.Obs,
            "OBS Studio",
            ["%PROGRAMFILES%\\obs-studio\\bin\\64bit\\obs64.exe"],
            ["%PROGRAMFILES%\\obs-studio"],
            ["obs"],
            [],
            [],
            [new("obs64", mode => mode == AppProfileMode.Streaming ? ProcessPriorityClass.AboveNormal : ProcessPriorityClass.Normal)],
            "OBS يديره Stream Director؛ App Intelligence لا يغير Encoder أو Scene Collection من وراء المستخدم."),

        new(
            ManagedAppId.TikTokLiveStudio,
            "TikTok LIVE Studio",
            [],
            [],
            ["tiktok", "live studio"],
            [],
            [],
            [new("TikTok LIVE Studio", mode => mode == AppProfileMode.Streaming ? ProcessPriorityClass.Normal : ProcessPriorityClass.BelowNormal)],
            "الأولوية فقط إذا تم اكتشاف Process مطابقة؛ لا يغير إعدادات البث proprietary."),

        new(
            ManagedAppId.Chrome,
            "Google Chrome",
            ["%PROGRAMFILES%\\Google\\Chrome\\Application\\chrome.exe", "%PROGRAMFILESX86%\\Google\\Chrome\\Application\\chrome.exe", "%LOCALAPPDATA%\\Google\\Chrome\\Application\\chrome.exe"],
            [],
            ["chrome"],
            [],
            [],
            [new("chrome", mode => mode == AppProfileMode.Gaming ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal)],
            "Gaming Profile يخفض Priority فقط؛ لا يغلق tabs ولا يمس بيانات المتصفح."),

        new(
            ManagedAppId.Edge,
            "Microsoft Edge",
            ["%PROGRAMFILESX86%\\Microsoft\\Edge\\Application\\msedge.exe", "%PROGRAMFILES%\\Microsoft\\Edge\\Application\\msedge.exe"],
            [],
            ["msedge"],
            [],
            [],
            [new("msedge", mode => mode == AppProfileMode.Gaming ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal)],
            "Gaming Profile يخفض Priority فقط؛ لا يغلق tabs ولا يمس بيانات المتصفح.")
    ];
}
