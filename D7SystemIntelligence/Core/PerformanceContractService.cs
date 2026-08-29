using System.Text.Json;

namespace D7SystemIntelligence.Core;

public sealed class PerformanceContractSettings
{
    public bool Enabled { get; set; }
    public int TargetFps { get; set; } = 144;
    public int TargetOnePercentLow { get; set; } = 100;
    public double MaxP99FrameMs { get; set; } = 20;
    public double MaxCpuTemp { get; set; } = 85;
    public double MaxGpuTemp { get; set; } = 82;
    public double MaxRamLoad { get; set; } = 90;
    public double MaxPingMs { get; set; } = 80;
    public bool AutoSafeMemoryClean { get; set; } = true;
    public bool AutoSmartFans { get; set; } = true;
    public bool ProtectFpsOverCapture { get; set; } = true;
}

public sealed class PerformanceContractSettingsStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public PerformanceContractSettingsStore()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D7SystemIntelligence", "Policies");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "performance-contract.json");
    }

    public PerformanceContractSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return Normalize(new());
            return Normalize(JsonSerializer.Deserialize<PerformanceContractSettings>(File.ReadAllText(_path), Options) ?? new());
        }
        catch { return Normalize(new()); }
    }

    public void Save(PerformanceContractSettings settings)
        => File.WriteAllText(_path, JsonSerializer.Serialize(Normalize(settings), Options));

    private static PerformanceContractSettings Normalize(PerformanceContractSettings s)
    {
        s.TargetFps = Math.Clamp(s.TargetFps, 30, 500);
        s.TargetOnePercentLow = Math.Clamp(s.TargetOnePercentLow, 20, s.TargetFps);
        s.MaxP99FrameMs = Math.Clamp(s.MaxP99FrameMs, 5, 100);
        s.MaxCpuTemp = Math.Clamp(s.MaxCpuTemp, 60, 95);
        s.MaxGpuTemp = Math.Clamp(s.MaxGpuTemp, 55, 92);
        s.MaxRamLoad = Math.Clamp(s.MaxRamLoad, 60, 98);
        s.MaxPingMs = Math.Clamp(s.MaxPingMs, 15, 500);
        return s;
    }
}

public sealed class PerformanceContractService : IDisposable
{
    private readonly GameSessionService _sessions;
    private readonly HardwareEngine _hardware;
    private readonly ShadowCaptureService _shadow;
    private readonly BackgroundAppManagerService _background = new();
    private readonly SmartFanController _fans;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PerformanceContractSettings _settings = new();
    private DateTimeOffset _lastMemoryAction = DateTimeOffset.MinValue;
    private DateTimeOffset _lastCaptureAction = DateTimeOffset.MinValue;
    private int _fpsViolationStreak;
    private bool _fansStartedByContract;

    public event Action<string>? StatusChanged;
    public bool IsRunning { get; private set; }
    public string LastStatus { get; private set; } = "Performance Contract متوقف.";

    public PerformanceContractService(GameSessionService sessions, HardwareEngine hardware, ShadowCaptureService shadow)
    {
        _sessions = sessions;
        _hardware = hardware;
        _shadow = shadow;
        _fans = new SmartFanController(hardware);
        _fans.StatusChanged += x => Publish("Fans • " + x);
    }

    public void Start(PerformanceContractSettings settings)
    {
        Stop(restoreFans: true);
        _settings = settings;
        if (!settings.Enabled)
        {
            Publish("Performance Contract محفوظ لكنه OFF.");
            return;
        }
        _fpsViolationStreak = 0;
        _sessions.SampleUpdated += OnSample;
        IsRunning = true;
        Publish($"Performance Contract ON • FPS ≥ {settings.TargetFps} • 1% ≥ {settings.TargetOnePercentLow} • P99 ≤ {settings.MaxP99FrameMs:0.#}ms");
    }

    public void Stop(bool restoreFans = true)
    {
        if (IsRunning) _sessions.SampleUpdated -= OnSample;
        IsRunning = false;
        _fpsViolationStreak = 0;
        if (_fansStartedByContract && restoreFans)
        {
            _fans.Stop(true);
            _fansStartedByContract = false;
        }
    }

    private void OnSample(GameSessionSample sample)
        => _ = HandleSampleAsync(sample);

    private async Task HandleSampleAsync(GameSessionSample s)
    {
        if (!IsRunning || !await _gate.WaitAsync(0)) return;
        try
        {
            var violations = new List<string>();
            if (s.Fps is { } fps && fps < _settings.TargetFps) violations.Add($"FPS {fps:0} < {_settings.TargetFps}");
            if (s.OnePercentLow is { } low && low < _settings.TargetOnePercentLow) violations.Add($"1% {low:0} < {_settings.TargetOnePercentLow}");
            if (s.P99FrameMs is { } p99 && p99 > _settings.MaxP99FrameMs) violations.Add($"P99 {p99:0.0}ms > {_settings.MaxP99FrameMs:0.0}");
            if (s.CpuTemp > _settings.MaxCpuTemp) violations.Add($"CPU {s.CpuTemp:0}°C");
            if (s.GpuTemp > _settings.MaxGpuTemp) violations.Add($"GPU {s.GpuTemp:0}°C");
            if (s.RamLoad > _settings.MaxRamLoad) violations.Add($"RAM {s.RamLoad:0}%");
            if (s.PingMs is { } ping && ping > _settings.MaxPingMs) violations.Add($"Ping {ping:0}ms");

            if (_settings.AutoSmartFans && (s.CpuTemp > _settings.MaxCpuTemp || s.GpuTemp > _settings.MaxGpuTemp) && !_fans.IsRunning)
            {
                var writable = _hardware.Read().Fans.Count(x => x.Controllable);
                if (writable > 0)
                {
                    _fansStartedByContract = _fans.Start();
                    if (_fansStartedByContract) violations.Add("Action: Smart Fans started");
                }
            }

            if (_settings.AutoSafeMemoryClean && s.RamLoad > _settings.MaxRamLoad && (DateTimeOffset.Now - _lastMemoryAction).TotalSeconds >= 90)
            {
                _lastMemoryAction = DateTimeOffset.Now;
                var clean = await _background.SmartCleanAsync();
                violations.Add("Action: " + clean.Replace(Environment.NewLine, " "));
            }

            var severeFps = s.Fps is { } f && f < _settings.TargetFps * .80 && s.GpuLoad >= 97;
            _fpsViolationStreak = severeFps ? _fpsViolationStreak + 1 : 0;
            if (_settings.ProtectFpsOverCapture && _fpsViolationStreak >= 5 && (DateTimeOffset.Now - _lastCaptureAction).TotalSeconds >= 120)
            {
                _lastCaptureAction = DateTimeOffset.Now;
                try
                {
                    var capture = await _shadow.GetStatusAsync();
                    if (capture.ReplayActive)
                    {
                        var stopped = await _shadow.StopAsync();
                        violations.Add("Action: أوقف D7 Shadow Capture لحماية الأداء • " + stopped);
                    }
                }
                catch (Exception ex) { violations.Add("Capture Guard: " + ex.Message); }
                _fpsViolationStreak = 0;
            }

            Publish(violations.Count == 0
                ? $"CONTRACT OK • FPS {(s.Fps?.ToString("0") ?? "—")} • 1% {(s.OnePercentLow?.ToString("0") ?? "—")} • P99 {(s.P99FrameMs?.ToString("0.0") ?? "—")}ms • Ping {(s.PingMs?.ToString("0") ?? "—")}ms"
                : "CONTRACT • " + string.Join(" • ", violations));
        }
        catch (Exception ex) { Publish("Performance Contract: " + ex.Message); }
        finally { _gate.Release(); }
    }

    private void Publish(string text)
    {
        LastStatus = text;
        try { StatusChanged?.Invoke(text); } catch { }
    }

    public void Dispose()
    {
        Stop(true);
        _fans.Dispose();
        _gate.Dispose();
    }
}
