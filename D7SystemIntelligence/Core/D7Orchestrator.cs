using System.Diagnostics;

namespace D7SystemIntelligence.Core;

public sealed class D7Orchestrator
{
    private readonly PolicyEngine _policy = new();
    private readonly TelemetryStore _telemetry = new();
    private readonly object _gate = new();
    private HashSet<string> _knownGameProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "cod26-cod", "cod", "eldenring", "valorant-win64-shipping", "cs2", "fortniteclient-win64-shipping"
    };

    public D7Profile Profile { get; set; } = D7Profile.Balanced;
    public OrchestratorStatus? LastStatus { get; private set; }
    public string TelemetryPath => _telemetry.RootPath;

    public void SetKnownGames(IEnumerable<GameRecord> games)
    {
        var names = games
            .Select(g => g.ExecutablePath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFileNameWithoutExtension(p!))
            .Where(n => !string.IsNullOrWhiteSpace(n));

        lock (_gate)
        {
            foreach (var n in names) _knownGameProcesses.Add(n!);
        }
    }

    public async Task<OrchestratorStatus> ObserveAsync(HardwareSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        D7RuntimeBus.PublishHardware(snapshot);

        var running = GetRunningProcessNames();
        var obs = running.Any(n => n.Equals("obs64", StringComparison.OrdinalIgnoreCase) || n.Equals("obs", StringComparison.OrdinalIgnoreCase));
        var tiktok = running.Any(n => n.Contains("tiktok", StringComparison.OrdinalIgnoreCase));
        var game = FindRunningGame(running);

        var mode = game != null && (obs || tiktok) ? D7RuntimeMode.StreamGaming
            : game != null ? D7RuntimeMode.Gaming
            : (obs || tiktok) ? D7RuntimeMode.Streaming
            : snapshot.CpuLoad < 6 && snapshot.GpuLoad < 6 ? D7RuntimeMode.Idle
            : D7RuntimeMode.Desktop;

        var reason = mode switch
        {
            D7RuntimeMode.StreamGaming => $"تم اكتشاف لعبة + بث{(game != null ? $": {game}" : string.Empty)}",
            D7RuntimeMode.Gaming => $"تم اكتشاف لعبة{(game != null ? $": {game}" : string.Empty)}",
            D7RuntimeMode.Streaming => "تم اكتشاف برنامج بث بدون لعبة نشطة",
            D7RuntimeMode.Idle => "الجهاز في وضع خمول",
            _ => "استخدام مكتبي عادي"
        };

        var context = new RuntimeContext(mode, game, obs, tiktok, DateTimeOffset.Now, reason);
        D7RuntimeBus.PublishContext(context);
        var decisions = _policy.Evaluate(snapshot, context, Profile);
        var summary = BuildSummary(context, decisions);
        var status = new OrchestratorStatus(context, Profile, decisions, summary);
        LastStatus = status;

        var sample = new TelemetrySample(
            DateTimeOffset.Now,
            mode,
            game,
            Profile,
            snapshot.CpuLoad,
            snapshot.GpuLoad,
            snapshot.CpuTemp,
            snapshot.GpuTemp,
            snapshot.RamLoad,
            snapshot.VramLoad,
            snapshot.Fans.Count,
            snapshot.Fans.Count(f => f.Controllable));

        await _telemetry.AppendAsync(sample, cancellationToken);
        return status;
    }

    private string? FindRunningGame(HashSet<string> running)
    {
        lock (_gate)
        {
            return _knownGameProcesses.FirstOrDefault(running.Contains);
        }
    }

    private static HashSet<string> GetRunningProcessNames()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try { result.Add(p.ProcessName); }
            catch { }
            finally { p.Dispose(); }
        }
        return result;
    }

    private static string BuildSummary(RuntimeContext context, IReadOnlyList<PolicyDecision> decisions)
    {
        var critical = decisions.Count(d => d.Severity.Equals("Critical", StringComparison.OrdinalIgnoreCase));
        var warning = decisions.Count(d => d.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase));

        if (critical > 0) return $"يوجد {critical} تنبيه حرج يحتاج تدخل الآن";
        if (warning > 0) return $"النظام يعمل مع {warning} تنبيه يحتاج مراقبة";
        return context.Mode switch
        {
            D7RuntimeMode.StreamGaming => "اللعب والبث تحت مراقبة D7",
            D7RuntimeMode.Gaming => "اللعبة تحت مراقبة الأداء والاستقرار",
            D7RuntimeMode.Streaming => "البث تحت مراقبة D7",
            D7RuntimeMode.Idle => "الجهاز مستقر وفي وضع خمول",
            _ => "الجهاز يعمل بشكل طبيعي"
        };
    }

    public static string ModeArabic(D7RuntimeMode mode) => mode switch
    {
        D7RuntimeMode.Idle => "خمول",
        D7RuntimeMode.Desktop => "سطح المكتب",
        D7RuntimeMode.Gaming => "لعب",
        D7RuntimeMode.Streaming => "بث",
        D7RuntimeMode.StreamGaming => "لعب + بث",
        D7RuntimeMode.Maintenance => "صيانة",
        _ => mode.ToString()
    };
}