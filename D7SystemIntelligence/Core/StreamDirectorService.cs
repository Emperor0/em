using System.Diagnostics;
using System.Text.Json;

namespace D7SystemIntelligence.Core;

public sealed record StreamDirectorSnapshot(
    bool ObsRunning,
    bool Connected,
    bool TikTokRunning,
    bool Streaming,
    bool Recording,
    bool VirtualCamera,
    double ObsCpuUsage,
    double ObsMemoryMb,
    double ActiveFps,
    double AverageFrameRenderMs,
    long RenderSkippedFrames,
    long RenderTotalFrames,
    long OutputSkippedFrames,
    long OutputTotalFrames,
    double OutputCongestion,
    string Detail,
    double RenderSkipPercent = 0,
    double OutputSkipPercent = 0,
    string? Encoder = null,
    double? GameFps = null,
    double? GameOnePercentLow = null,
    double? GameP99FrameMs = null,
    double? CpuLoad = null,
    double? GpuLoad = null,
    double? RamLoad = null,
    double? PingMs = null,
    double? JitterMs = null,
    string Health = "Unknown",
    string Bottleneck = "Unknown",
    string Recommendation = "");

public sealed class StreamDirectorService : IAsyncDisposable
{
    private ObsWebSocketClient? _obs;
    private readonly ShadowCaptureSettingsStore _settingsStore = new();

    public async Task<StreamDirectorSnapshot> ReadAsync(CancellationToken token = default)
    {
        var obsRunning = HasProcess("obs64") || HasProcess("obs");
        var tiktok = Process.GetProcesses().Any(p => SafeName(p).Contains("tiktok", StringComparison.OrdinalIgnoreCase));
        if (!obsRunning)
        {
            return Build(false, false, tiktok, false, false, false, null, null, null,
                "OBS غير شغال. Stream Director لا يخمن حالة البث بدون مصدر OBS حقيقي.");
        }

        try
        {
            await EnsureConnectedAsync(token);
            var stats = await _obs!.GetStatsAsync(token);
            var stream = await SafeRequestAsync("GetStreamStatus", token);
            var record = await SafeRequestAsync("GetRecordStatus", token);
            var cam = await SafeRequestAsync("GetVirtualCamStatus", token);
            var streaming = B(stream, "outputActive");
            var recording = B(record, "outputActive");
            var virtualCam = B(cam, "outputActive");
            var congestion = D(stream, "outputCongestion");
            var encoder = await TryGetEncoderAsync(token);

            var snapshot = Build(
                true,
                true,
                tiktok,
                streaming,
                recording,
                virtualCam,
                stats,
                congestion,
                encoder,
                "OBS WebSocket متصل؛ Render/Output/Network metrics مأخوذة مباشرة من OBS، وGame metrics من D7KT Runtime Bus.");

            return snapshot;
        }
        catch (Exception ex)
        {
            return Build(true, false, tiktok, false, false, false, null, null, null,
                "تعذر الاتصال بـOBS WebSocket: " + ex.Message);
        }
    }

    private StreamDirectorSnapshot Build(
        bool obsRunning,
        bool connected,
        bool tiktok,
        bool streaming,
        bool recording,
        bool virtualCam,
        ObsRuntimeStats? stats,
        double? congestion,
        string? encoder,
        string detail)
    {
        var h = D7RuntimeBus.Hardware;
        var game = D7RuntimeBus.SessionSample;
        var renderSkip = stats?.RenderSkipPercent ?? 0;
        var outputSkip = stats?.OutputSkipPercent ?? 0;
        var networkCongestion = congestion ?? 0;
        var frameBudgetMs = stats?.ActiveFps > 1 ? 1000d / stats.ActiveFps : 16.667;
        var renderBudgetUse = stats == null || frameBudgetMs <= 0 ? 0 : stats.AverageFrameRenderTimeMs * 100d / frameBudgetMs;

        var issues = new List<string>();
        string bottleneck;
        string recommendation;

        if (!connected)
        {
            bottleneck = "OBS telemetry unavailable";
            recommendation = "فعّل OBS WebSocket أو صحح Host/Port/Password. D7KT لن يعطي تشخيص Stream بدون القياسات الفعلية.";
        }
        else if (networkCongestion >= .05)
        {
            bottleneck = "Network / ingest pressure";
            issues.Add($"Congestion {networkCongestion * 100:0.0}%");
            recommendation = "المشكلة الأساسية شبكة/ingest. افحص bitrate والـroute والـpacket loss قبل خفض جودة اللعبة.";
        }
        else if (renderSkip >= .5 || renderBudgetUse >= 85)
        {
            bottleneck = h?.GpuLoad >= 97 ? "GPU / OBS rendering pressure" : "OBS scene rendering pressure";
            issues.Add($"Render skip {renderSkip:0.00}%");
            if (renderBudgetUse >= 85) issues.Add($"Render budget {renderBudgetUse:0}%");
            recommendation = h?.GpuLoad >= 97
                ? "GPU قريب من التشبع وOBS يحتاج وقتًا لتركيب المشهد. اترك GPU headroom أو حد FPS اللعبة قبل خفض دقة البث عشوائيًا."
                : "راجع تعقيد Scene/Sources/Filters في OBS؛ Render lag لا يعني تلقائيًا مشكلة Encoder أو شبكة.";
        }
        else if (outputSkip >= .5)
        {
            bottleneck = "Encoding/output pressure";
            issues.Add($"Output skip {outputSkip:0.00}%");
            recommendation = "المشكلة في مسار الإخراج/الترميز أكثر من الشبكة. راجع Encoder preset/FPS/resolution وحمل CPU/GPU قبل أي تغيير آخر.";
        }
        else if (game?.P99FrameMs >= 33.3)
        {
            bottleneck = "Game frametime pressure";
            issues.Add($"Game P99 {game.P99FrameMs:0.0}ms");
            recommendation = "البث نفسه مستقر حاليًا لكن اللعبة فيها frametime spikes. راجع Stutter Black Box بدل تغيير OBS.";
        }
        else if (h?.CpuLoad >= 94)
        {
            bottleneck = "CPU headroom low";
            issues.Add($"CPU {h.CpuLoad:0}%");
            recommendation = "CPU headroom منخفض. راقب اللعبة + TikTok/OBS والعمليات الخلفية؛ لا ترفع Priorities عشوائيًا بدون A/B.";
        }
        else
        {
            bottleneck = "No active stream bottleneck detected";
            recommendation = "لا يوجد دليل حالي على Render/Encode/Network bottleneck. اترك الإعدادات كما هي بدل Tweaks بلا قياس.";
        }

        if (h?.GpuLoad >= 99 && !issues.Any(x => x.StartsWith("GPU", StringComparison.OrdinalIgnoreCase)))
            issues.Add($"GPU {h.GpuLoad:0}%");
        if (game?.JitterMs >= 12) issues.Add($"Jitter {game.JitterMs:0.0}ms");

        var health = !connected ? "Unavailable"
            : networkCongestion >= .10 || renderSkip >= 2 || outputSkip >= 2 ? "Critical"
            : issues.Count > 0 ? "Warning"
            : "Good";

        var pipeline = virtualCam && tiktok
            ? "Pipeline: OBS → Virtual Camera → TikTok LIVE Studio"
            : streaming ? "Pipeline: OBS direct stream"
            : recording ? "Pipeline: OBS recording"
            : virtualCam ? "Pipeline: OBS Virtual Camera"
            : "Outputs idle";

        return new StreamDirectorSnapshot(
            obsRunning,
            connected,
            tiktok,
            streaming,
            recording,
            virtualCam,
            stats?.CpuUsage ?? 0,
            stats?.MemoryUsageMb ?? 0,
            stats?.ActiveFps ?? 0,
            stats?.AverageFrameRenderTimeMs ?? 0,
            stats?.RenderSkippedFrames ?? 0,
            stats?.RenderTotalFrames ?? 0,
            stats?.OutputSkippedFrames ?? 0,
            stats?.OutputTotalFrames ?? 0,
            networkCongestion,
            detail + " " + pipeline,
            renderSkip,
            outputSkip,
            encoder,
            game?.Fps,
            game?.OnePercentLow,
            game?.P99FrameMs,
            h?.CpuLoad,
            h?.GpuLoad,
            h?.RamLoad,
            game?.PingMs,
            game?.JitterMs,
            health,
            bottleneck,
            recommendation);
    }

    private async Task EnsureConnectedAsync(CancellationToken token)
    {
        if (_obs?.IsConnected == true) return;
        if (_obs != null) await _obs.DisposeAsync();
        _obs = new ObsWebSocketClient();
        var settings = _settingsStore.Load();
        var password = WindowsCredentialStore.Read(ShadowCaptureService.ObsCredentialTarget);
        await _obs.ConnectAsync(settings.ObsHost, settings.ObsPort, password, token);
    }

    private async Task<string?> TryGetEncoderAsync(CancellationToken token)
    {
        try
        {
            var mode = await _obs!.GetProfileParameterAsync("Output", "Mode", token) ?? "Simple";
            var section = mode.Contains("Advanced", StringComparison.OrdinalIgnoreCase) || mode.Contains("Adv", StringComparison.OrdinalIgnoreCase)
                ? "AdvOut"
                : "SimpleOutput";
            return await _obs.GetProfileParameterAsync(section, "RecEncoder", token);
        }
        catch { return null; }
    }

    private async Task<JsonElement> SafeRequestAsync(string type, CancellationToken token)
    {
        try { return await _obs!.RequestAsync(type, cancellationToken: token); }
        catch { using var d = JsonDocument.Parse("{}"); return d.RootElement.Clone(); }
    }

    private static bool B(JsonElement e, string n)
        => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.True;

    private static double D(JsonElement e, string n)
        => e.TryGetProperty(n, out var p) && p.TryGetDouble(out var v) ? v : 0;

    private static bool HasProcess(string name)
    {
        var processes = Process.GetProcessesByName(name);
        try { return processes.Length > 0; }
        finally { foreach (var p in processes) p.Dispose(); }
    }

    private static string SafeName(Process p)
    {
        try { return p.ProcessName; }
        catch { return string.Empty; }
        finally { p.Dispose(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_obs != null) await _obs.DisposeAsync();
        _obs = null;
    }
}

public sealed class StreamProcessGovernor : IDisposable
{
    private readonly Dictionary<int, ProcessPriorityClass> _original = new();
    public bool Active => _original.Count > 0;
    public int ChangedCount => _original.Count;

    public string Apply(string? gameProcessName)
    {
        Restore();
        var changed = new List<string>();
        var already = new List<string>();
        if (!string.IsNullOrWhiteSpace(gameProcessName)) SetByName(gameProcessName, ProcessPriorityClass.AboveNormal, changed, already);
        SetByName("obs64", ProcessPriorityClass.AboveNormal, changed, already);
        SetByName("obs", ProcessPriorityClass.AboveNormal, changed, already);
        foreach (var p in Process.GetProcesses())
        {
            string name;
            try { name = p.ProcessName; }
            catch { p.Dispose(); continue; }
            if (name.Contains("tiktok", StringComparison.OrdinalIgnoreCase)) SetProcess(p, ProcessPriorityClass.Normal, changed, already);
            else p.Dispose();
        }
        foreach (var bg in new[] { "chrome", "msedge", "Discord" }) SetByName(bg, ProcessPriorityClass.BelowNormal, changed, already);

        if (changed.Count == 0 && already.Count == 0) return "Unsupported/Not Running • لم يجد D7KT عمليات مناسبة لـStream Governor.";
        if (changed.Count == 0) return "Already Optimal • كل العمليات المكتشفة على Priority المطلوبة بالفعل.\n" + string.Join(Environment.NewLine, already);
        var lines = new List<string> { "Applied + Verified • تم تغيير Priority للعمليات التالية:" };
        lines.AddRange(changed);
        if (already.Count > 0) { lines.Add("Already Optimal:"); lines.AddRange(already); }
        return string.Join(Environment.NewLine, lines);
    }

    public string Restore()
    {
        var count = 0;
        foreach (var kv in _original.ToArray())
        {
            try
            {
                using var p = Process.GetProcessById(kv.Key);
                if (!p.HasExited)
                {
                    p.PriorityClass = kv.Value;
                    if (p.PriorityClass == kv.Value) count++;
                }
            }
            catch { }
        }
        _original.Clear();
        return count > 0 ? $"Restore Verified • تمت استعادة Priority لـ{count} عملية." : "لا توجد Priorities غيّرها D7KT تحتاج استعادة.";
    }

    private void SetByName(string name, ProcessPriorityClass priority, List<string> changed, List<string> already)
    {
        foreach (var p in Process.GetProcessesByName(name)) SetProcess(p, priority, changed, already);
    }

    private void SetProcess(Process p, ProcessPriorityClass priority, List<string> changed, List<string> already)
    {
        using (p)
        {
            try
            {
                var before = p.PriorityClass;
                if (before == priority) { already.Add($"{p.ProcessName}: {priority}"); return; }
                p.PriorityClass = priority;
                var after = p.PriorityClass;
                if (after == priority)
                {
                    _original[p.Id] = before;
                    changed.Add($"{p.ProcessName}: {before} → {after}");
                }
            }
            catch { }
        }
    }

    public void Dispose() => Restore();
}
