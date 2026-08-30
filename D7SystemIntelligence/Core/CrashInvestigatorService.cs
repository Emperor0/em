using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace D7SystemIntelligence.Core;

public sealed record StabilityEventRecord(
    DateTime TimeCreated,
    string Log,
    string Provider,
    int EventId,
    string Level,
    string Category,
    string Summary,
    string Message);

public sealed record CrashCorrelationRecord(
    DateTime Start,
    DateTime End,
    string Signal,
    int EventCount,
    IReadOnlyList<string> Categories,
    string Interpretation,
    string Evidence);

public sealed record CrashInvestigationReport(
    DateTime From,
    DateTime To,
    IReadOnlyList<StabilityEventRecord> Events,
    int AppCrashes,
    int HardwareErrors,
    int GpuDriverEvents,
    int StorageEvents,
    int UnexpectedShutdowns,
    string Verdict)
{
    public IReadOnlyList<CrashCorrelationRecord> Correlations { get; init; } = [];
}

public sealed class CrashInvestigatorService
{
    public async Task<CrashInvestigationReport> ScanAsync(TimeSpan lookback, CancellationToken cancellationToken = default)
    {
        lookback = lookback < TimeSpan.FromHours(1) ? TimeSpan.FromHours(1) : lookback > TimeSpan.FromDays(30) ? TimeSpan.FromDays(30) : lookback;
        var from = DateTime.Now - lookback;
        var to = DateTime.Now;
        var hours = Math.Max(1, (int)Math.Ceiling(lookback.TotalHours));

        var script = $@"
$start=(Get-Date).AddHours(-{hours})
$rows=@()
$logs=@('System','Application')
foreach($log in $logs){{
  Get-WinEvent -FilterHashtable @{{LogName=$log; StartTime=$start; Level=1,2,3}} -ErrorAction SilentlyContinue | ForEach-Object {{
    $provider=$_.ProviderName
    $id=[int]$_.Id
    $take=$false
    if($log -eq 'Application' -and ($id -in 1000,1001,1002,1026)){{$take=$true}}
    if($provider -match 'WHEA|nvlddmkm|Display|Disk|storahci|stornvme|Ntfs|volmgr|Kernel-Power'){{$take=$true}}
    if($id -in 1,7,11,14,15,17,18,19,20,41,46,51,55,129,153,157,4101){{$take=$true}}
    if($take){{
      $rows += [pscustomobject]@{{TimeCreated=$_.TimeCreated;Log=$log;Provider=$provider;Id=$id;Level=$_.LevelDisplayName;Message=$_.Message}}
    }}
  }}
}}
$rows | Sort-Object TimeCreated -Descending | Select-Object -First 300 | ConvertTo-Json -Depth 4 -Compress
";

        var run = await RunPowerShellAsync(script, cancellationToken);
        var events = new List<StabilityEventRecord>();
        if (run.ExitCode == 0 && !string.IsNullOrWhiteSpace(run.Output))
        {
            try
            {
                using var doc = JsonDocument.Parse(run.Output);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (var e in doc.RootElement.EnumerateArray()) events.Add(Parse(e));
                else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    events.Add(Parse(doc.RootElement));
            }
            catch { }
        }

        var apps = events.Count(x => x.Category == "Application Crash");
        var whea = events.Count(x => x.Category == "Hardware / WHEA");
        var gpu = events.Count(x => x.Category == "GPU / Display");
        var storage = events.Count(x => x.Category == "Storage");
        var shutdown = events.Count(x => x.Category == "Unexpected Shutdown");
        var correlations = BuildCorrelations(events);
        var verdict = BuildVerdict(apps, whea, gpu, storage, shutdown, events.Count, correlations);
        return new CrashInvestigationReport(from, to, events, apps, whea, gpu, storage, shutdown, verdict)
        {
            Correlations = correlations
        };
    }

    private static IReadOnlyList<CrashCorrelationRecord> BuildCorrelations(IReadOnlyList<StabilityEventRecord> source)
    {
        var ordered = source.Where(x => x.TimeCreated != DateTime.MinValue).OrderBy(x => x.TimeCreated).ToArray();
        if (ordered.Length < 2) return [];

        var clusters = new List<List<StabilityEventRecord>>();
        var current = new List<StabilityEventRecord> { ordered[0] };
        for (var i = 1; i < ordered.Length; i++)
        {
            var gap = ordered[i].TimeCreated - current[^1].TimeCreated;
            if (gap <= TimeSpan.FromMinutes(7)) current.Add(ordered[i]);
            else
            {
                if (IsUsefulCluster(current)) clusters.Add(current);
                current = [ordered[i]];
            }
        }
        if (IsUsefulCluster(current)) clusters.Add(current);

        return clusters.Select(BuildCorrelation)
            .OrderByDescending(x => CorrelationRank(x.Signal))
            .ThenByDescending(x => x.End)
            .Take(20)
            .ToArray();
    }

    private static bool IsUsefulCluster(IReadOnlyList<StabilityEventRecord> cluster)
        => cluster.Count >= 2 && cluster.Select(x => x.Category).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2;

    private static CrashCorrelationRecord BuildCorrelation(IReadOnlyList<StabilityEventRecord> cluster)
    {
        var cats = cluster.Select(x => x.Category).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var hasWhea = cats.Contains("Hardware / WHEA", StringComparer.OrdinalIgnoreCase);
        var hasGpu = cats.Contains("GPU / Display", StringComparer.OrdinalIgnoreCase);
        var hasStorage = cats.Contains("Storage", StringComparer.OrdinalIgnoreCase);
        var hasShutdown = cats.Contains("Unexpected Shutdown", StringComparer.OrdinalIgnoreCase);
        var hasApp = cats.Contains("Application Crash", StringComparer.OrdinalIgnoreCase);

        string signal;
        string interpretation;
        if (hasWhea && hasShutdown)
        {
            signal = "WHEA + Shutdown";
            interpretation = "Hardware/WHEA ظهر قريبًا من إيقاف غير متوقع. هذا يرفع أولوية فحص استقرار CPU/RAM/PCIe/OC، لكنه لا يثبت القطعة المسببة وحده.";
        }
        else if (hasWhea && hasGpu)
        {
            signal = "WHEA + GPU";
            interpretation = "WHEA وGPU/Display events متقاربان. افحص استقرار PCIe/GPU/CPU/RAM والتعريف قبل لوم اللعبة وحدها.";
        }
        else if (hasStorage && hasShutdown)
        {
            signal = "Storage + Shutdown";
            interpretation = "Storage events ظهرت قرب Kernel-Power/Shutdown. راجع Storage Center والكابلات/القرص/الدرايفر، مع تذكر أن Event 41 لا يحدد السبب وحده.";
        }
        else if (hasGpu && hasApp)
        {
            signal = "GPU + App Crash";
            interpretation = "GPU/Display event ظهر قرب Application crash. الاحتمال يستحق مقارنة Driver version ووقت جلسة اللعب، بدون افتراض أن التعريف هو السبب الوحيد.";
        }
        else if (hasStorage && hasApp)
        {
            signal = "Storage + App Crash";
            interpretation = "Storage I/O event ظهر قرب تعطل تطبيق. افحص Reliability counters والمساحة/القرص قبل اعتبار العطل Software-only.";
        }
        else
        {
            signal = "Temporal Cluster";
            interpretation = "عدة فئات أحداث ظهرت ضمن نافذة زمنية قصيرة. هذه Correlation فقط، وليست إثبات Causation.";
        }

        var evidence = string.Join(Environment.NewLine,
            cluster.OrderBy(x => x.TimeCreated).Select(x => $"{x.TimeCreated:HH:mm:ss} • {x.Category} • {x.Provider} • Event {x.EventId}"));
        return new CrashCorrelationRecord(cluster.Min(x => x.TimeCreated), cluster.Max(x => x.TimeCreated), signal, cluster.Count, cats, interpretation, evidence);
    }

    private static int CorrelationRank(string signal) => signal switch
    {
        "WHEA + Shutdown" => 100,
        "WHEA + GPU" => 90,
        "Storage + Shutdown" => 80,
        "GPU + App Crash" => 70,
        "Storage + App Crash" => 60,
        _ => 10
    };

    private static StabilityEventRecord Parse(JsonElement e)
    {
        static string S(JsonElement el, string n) => el.TryGetProperty(n, out var p) && p.ValueKind != JsonValueKind.Null ? p.ToString() : string.Empty;
        static int I(JsonElement el, string n) => el.TryGetProperty(n, out var p) && p.TryGetInt32(out var v) ? v : 0;
        var provider = S(e, "Provider");
        var id = I(e, "Id");
        var message = Clean(S(e, "Message"));
        var time = DateTime.TryParse(S(e, "TimeCreated"), out var t) ? t : DateTime.MinValue;
        var category = Classify(provider, id);
        var summary = message.Length > 180 ? message[..180] + "…" : message;
        return new StabilityEventRecord(time, S(e, "Log"), provider, id, S(e, "Level"), category, summary, message);
    }

    private static string Classify(string provider, int id)
    {
        var p = provider.ToLowerInvariant();
        if (p.Contains("whea")) return "Hardware / WHEA";
        if (p.Contains("nvlddmkm") || p == "display" || id == 4101) return "GPU / Display";
        if (p.Contains("disk") || p.Contains("storahci") || p.Contains("stornvme") || p.Contains("ntfs") || p.Contains("volmgr") || id is 7 or 11 or 51 or 55 or 129 or 153 or 157) return "Storage";
        if (p.Contains("kernel-power") && id == 41) return "Unexpected Shutdown";
        if (id is 1000 or 1001 or 1002 or 1026) return "Application Crash";
        return "System";
    }

    private static string BuildVerdict(int apps, int whea, int gpu, int storage, int shutdown, int total, IReadOnlyList<CrashCorrelationRecord> correlations)
    {
        var strongest = correlations.FirstOrDefault();
        if (strongest != null && CorrelationRank(strongest.Signal) >= 60)
            return $"أقوى Correlation: {strongest.Signal} • {strongest.Start:yyyy-MM-dd HH:mm:ss} → {strongest.End:HH:mm:ss}. {strongest.Interpretation}";
        if (whea > 0) return $"تنبيه مهم: {whea} WHEA/Hardware event. افحص استقرار CPU/RAM/PCIe قبل اعتبار المشكلة برمجية.";
        if (storage > 0) return $"تم رصد {storage} Storage event. راجع Storage Center والصحة/الكابلات/الدرايفر قبل تجاهلها.";
        if (gpu > 0) return $"تم رصد {gpu} GPU/Display driver event. قارن توقيتها بجلسات اللعب والتعريف المستخدم.";
        if (shutdown > 0) return $"تم رصد {shutdown} إيقاف غير متوقع/Kernel-Power. Event 41 يثبت الانقطاع غير السليم لكنه لا يحدد السبب وحده.";
        if (apps > 0) return $"الهاردوير لا يظهر خطأ واضح في هذه النافذة، لكن يوجد {apps} Application crash/hang.";
        return total == 0 ? "لا توجد أحداث استقرار مهمة في الفترة المحددة." : $"يوجد {total} حدث تحذير/خطأ تمت تصفيته، بدون نمط حرج واضح من التصنيفات الحالية.";
    }

    private static string Clean(string value)
        => string.Join(' ', (value ?? string.Empty).Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static async Task<(int ExitCode, string Output, string Error)> RunPowerShellAsync(string script, CancellationToken token)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        p.Start();
        var stdout = p.StandardOutput.ReadToEndAsync(token);
        var stderr = p.StandardError.ReadToEndAsync(token);
        await p.WaitForExitAsync(token);
        return (p.ExitCode, (await stdout).Trim(), (await stderr).Trim());
    }
}
