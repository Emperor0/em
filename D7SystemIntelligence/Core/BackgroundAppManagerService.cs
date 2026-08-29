using System.Diagnostics;
using System.Text.Json;

namespace D7SystemIntelligence.Core;

public enum BackgroundProcessDecision
{
    Protected,
    Keep,
    Review,
    SafeToClose
}

public sealed record BackgroundProcessRecord(
    int ProcessId,
    string Name,
    string ExecutablePath,
    string Publisher,
    double MemoryMb,
    double CpuPercent,
    bool HasVisibleWindow,
    BackgroundProcessDecision Decision,
    string DecisionText,
    string Reason,
    bool CanClose);

internal sealed class BackgroundPolicyStore
{
    public HashSet<string> AlwaysClose { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> AlwaysKeep { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class BackgroundAppManagerService
{
    private readonly string _policyPath;
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System","Idle","Registry","Memory Compression","Secure System","smss","csrss","wininit","winlogon",
        "services","lsass","svchost","dwm","fontdrvhost","sihost","taskhostw","ctfmon","audiodg","explorer",
        "WmiPrvSE","SearchIndexer","SearchApp","StartMenuExperienceHost","ShellExperienceHost","RuntimeBroker",
        "SecurityHealthService","SecurityHealthSystray","MsMpEng","NisSrv","spoolsv","conhost"
    };

    private static readonly string[] KeepTokens =
    {
        "easyanticheat","battleye","beservice","vgc","vgtray","riotclientservices","faceit","anticheat",
        "nvcontainer","nvidia","amd","radeon","realtek","nahimic","steelseries","sonar","astro","logitech",
        "obs64","obs32","tiktok","steamservice","gamingservices","gameinput","wireguard","openvpn"
    };

    private static readonly string[] SafeBackgroundTokens =
    {
        "adobeipcbroker","ccxprocess","adobecollabsync","creative cloud helper","adobe cef helper",
        "onedrivestandaloneupdater","microsoftedgeupdate","googleupdate","google updater","updateassistant",
        "phoneexperiencehost","yourphone","copilot"
    };

    public BackgroundAppManagerService()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D7SystemIntelligence", "Policies");
        Directory.CreateDirectory(root);
        _policyPath = Path.Combine(root, "background-apps.json");
    }

    public async Task<IReadOnlyList<BackgroundProcessRecord>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var first = CaptureCpuTimes();
        var started = Stopwatch.GetTimestamp();
        await Task.Delay(400, cancellationToken);
        var elapsed = (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
        var second = CaptureCpuTimes();
        var policy = LoadPolicy();
        var currentSession = Process.GetCurrentProcess().SessionId;
        var ownPid = Environment.ProcessId;
        var list = new List<BackgroundProcessRecord>();

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == ownPid || p.Id <= 4 || p.SessionId != currentSession) continue;
                var name = p.ProcessName;
                var path = TryGetPath(p);
                var publisher = TryGetPublisher(path);
                var hasWindow = p.MainWindowHandle != IntPtr.Zero;
                var memory = Math.Max(0, p.WorkingSet64) / 1024d / 1024d;
                var cpu = 0d;
                if (first.TryGetValue(p.Id, out var a) && second.TryGetValue(p.Id, out var b) && elapsed > 0)
                    cpu = Math.Clamp((b - a).TotalSeconds / elapsed / Environment.ProcessorCount * 100d, 0, 100);

                var classification = Classify(name, path, publisher, hasWindow, policy);
                list.Add(new BackgroundProcessRecord(
                    p.Id, name, path, publisher, memory, cpu, hasWindow,
                    classification.Decision, DecisionArabic(classification.Decision), classification.Reason,
                    classification.Decision is BackgroundProcessDecision.SafeToClose or BackgroundProcessDecision.Review));
            }
            catch { }
            finally { p.Dispose(); }
        }

        return list
            .OrderBy(x => x.Decision)
            .ThenByDescending(x => x.MemoryMb)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<string> CloseAsync(int processId, bool allowReview = false, CancellationToken cancellationToken = default)
    {
        var item = (await ScanAsync(cancellationToken)).FirstOrDefault(x => x.ProcessId == processId);
        if (item == null) return "العملية انتهت أو لم تعد موجودة.";
        if (!item.CanClose) return $"D7 رفض إغلاق {item.Name}: {item.Reason}";
        if (item.Decision == BackgroundProcessDecision.Review && !allowReview)
            return "هذه العملية تحتاج تأكيد يدوي قبل الإغلاق.";

        try
        {
            using var p = Process.GetProcessById(processId);
            if (p.MainWindowHandle != IntPtr.Zero)
            {
                try { p.CloseMainWindow(); } catch { }
                try { await p.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(2), cancellationToken); } catch { }
            }
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
                await p.WaitForExitAsync(cancellationToken);
            }
            return $"تم إغلاق {item.Name} • تم تحرير قرابة {item.MemoryMb:0} MB من الذاكرة المستخدمة وقت الفحص.";
        }
        catch (Exception ex)
        {
            return $"تعذر إغلاق {item.Name}: {ex.Message}";
        }
    }

    public async Task<string> SmartCleanAsync(CancellationToken cancellationToken = default)
    {
        var scan = await ScanAsync(cancellationToken);
        var safe = scan.Where(x => x.Decision == BackgroundProcessDecision.SafeToClose && !x.HasVisibleWindow).ToArray();
        var closed = 0;
        var reclaimed = 0d;
        var names = new List<string>();
        foreach (var item in safe)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await CloseAsync(item.ProcessId, false, cancellationToken);
            if (result.StartsWith("تم إغلاق", StringComparison.Ordinal))
            {
                closed++;
                reclaimed += item.MemoryMb;
                names.Add(item.Name);
            }
        }
        return closed == 0
            ? "لم يجد D7 تطبيقات خلفية مصنفة Safe-To-Close الآن. لم يتم لمس أي عملية تحتاج مراجعة."
            : $"تم إغلاق {closed} عملية خلفية آمنة • الذاكرة المستخدمة قبل الإغلاق ≈ {reclaimed:0} MB\n" + string.Join("، ", names.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    public string SetPolicy(BackgroundProcessRecord item, bool alwaysClose)
    {
        var key = PolicyKey(item.Name, item.ExecutablePath);
        var policy = LoadPolicy();
        if (alwaysClose)
        {
            policy.AlwaysKeep.Remove(key);
            policy.AlwaysClose.Add(key);
        }
        else
        {
            policy.AlwaysClose.Remove(key);
            policy.AlwaysKeep.Add(key);
        }
        SavePolicy(policy);
        return alwaysClose ? $"D7 سيتعامل مع {item.Name} كتطبيق خلفية قابل للإغلاق." : $"تمت حماية {item.Name} من Smart Clean.";
    }

    public string ClearPolicy(BackgroundProcessRecord item)
    {
        var key = PolicyKey(item.Name, item.ExecutablePath);
        var policy = LoadPolicy();
        policy.AlwaysClose.Remove(key);
        policy.AlwaysKeep.Remove(key);
        SavePolicy(policy);
        return "تمت إعادة القرار للوضع الذكي الافتراضي.";
    }

    private static (BackgroundProcessDecision Decision, string Reason) Classify(string name, string path, string publisher, bool hasWindow, BackgroundPolicyStore policy)
    {
        var key = PolicyKey(name, path);
        if (policy.AlwaysKeep.Contains(key)) return (BackgroundProcessDecision.Keep, "محمي حسب اختيارك السابق.");
        if (ProtectedNames.Contains(name)) return (BackgroundProcessDecision.Protected, "عملية Windows/جلسة أساسية ولا يسمح D7 بإغلاقها.");
        if (IsUnderWindows(path)) return (BackgroundProcessDecision.Protected, "الملف التنفيذي داخل مجلد Windows؛ Smart Clean لا يلمسه.");

        var combined = (name + " " + path + " " + publisher).ToLowerInvariant();
        if (KeepTokens.Any(combined.Contains)) return (BackgroundProcessDecision.Keep, "مرتبط بتعريف/صوت/شبكة/بث/Anti-Cheat أو طبقة تشغيل قد تحتاجها الجلسة.");
        if (policy.AlwaysClose.Contains(key)) return (BackgroundProcessDecision.SafeToClose, "مصنف قابلًا للإغلاق حسب اختيارك السابق.");
        if (hasWindow) return (BackgroundProcessDecision.Keep, "لديه نافذة ظاهرة؛ D7 يعتبره مستخدمًا الآن ولا يغلقه تلقائيًا.");
        if (SafeBackgroundTokens.Any(combined.Contains)) return (BackgroundProcessDecision.SafeToClose, "Helper/Updater معروف ويعمل بلا نافذة؛ يمكن إغلاقه بدون تعطيل Windows نفسه.");
        if (!string.IsNullOrWhiteSpace(path) && (IsUnder(path, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)) || IsUnder(path, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))))
            return (BackgroundProcessDecision.Review, "تطبيق مستخدم يعمل بالخلفية. يمكن إغلاقه يدويًا، لكن D7 لن يفترض أنه غير مهم.");
        return (BackgroundProcessDecision.Keep, "لم يحصل D7 على ثقة كافية لإغلاقه تلقائيًا.");
    }

    private static Dictionary<int, TimeSpan> CaptureCpuTimes()
    {
        var map = new Dictionary<int, TimeSpan>();
        foreach (var p in Process.GetProcesses())
        {
            try { map[p.Id] = p.TotalProcessorTime; } catch { }
            finally { p.Dispose(); }
        }
        return map;
    }

    private static string TryGetPath(Process p)
    {
        try { return p.MainModule?.FileName ?? string.Empty; } catch { return string.Empty; }
    }

    private static string TryGetPublisher(string path)
    {
        try { return string.IsNullOrWhiteSpace(path) ? string.Empty : FileVersionInfo.GetVersionInfo(path).CompanyName ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static bool IsUnderWindows(string path)
        => IsUnder(path, Environment.GetFolderPath(Environment.SpecialFolder.Windows));

    private static bool IsUnder(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return false;
        try
        {
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(basePath, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static string PolicyKey(string name, string path) => (name + "|" + path).ToLowerInvariant();
    private static string DecisionArabic(BackgroundProcessDecision value) => value switch
    {
        BackgroundProcessDecision.Protected => "محمي",
        BackgroundProcessDecision.Keep => "خله شغال",
        BackgroundProcessDecision.SafeToClose => "آمن للإغلاق",
        _ => "راجع"
    };

    private BackgroundPolicyStore LoadPolicy()
    {
        try
        {
            if (!File.Exists(_policyPath)) return new();
            return JsonSerializer.Deserialize<BackgroundPolicyStore>(File.ReadAllText(_policyPath)) ?? new();
        }
        catch { return new(); }
    }

    private void SavePolicy(BackgroundPolicyStore policy)
        => File.WriteAllText(_policyPath, JsonSerializer.Serialize(policy, new JsonSerializerOptions { WriteIndented = true }));
}
