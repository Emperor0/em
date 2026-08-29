using System.Text.Json;

namespace D7SystemIntelligence.Core;

public sealed class ShadowCaptureSettings
{
    public bool Enabled { get; set; }
    public int ReplaySeconds { get; set; } = 60;
    public string SaveFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "D7 Clips");
    public int MaxGpuBudgetPercent { get; set; } = 3;
    public int MaxFpsLoss { get; set; } = 3;
    public int BitrateMbps { get; set; } = 20;
    public int TargetFps { get; set; } = 60;
    public string PreferredEncoder { get; set; } = "Auto";
    public bool CaptureMicrophone { get; set; }
    public bool CaptureGameAudio { get; set; } = true;
    public bool CaptureDiscordTrack { get; set; }
    public bool AutoCleanup { get; set; } = true;
    public int MaxLibraryGb { get; set; } = 100;
    public string SaveHotkey { get; set; } = "F8";
    public bool ShowMinimalIndicator { get; set; } = true;
}

public sealed class ShadowCaptureSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;

    public ShadowCaptureSettingsStore()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "D7SystemIntelligence");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "shadow-capture.json");
    }

    public ShadowCaptureSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return Normalize(new ShadowCaptureSettings());
            var json = File.ReadAllText(_path);
            var settings = JsonSerializer.Deserialize<ShadowCaptureSettings>(json, JsonOptions)
                           ?? new ShadowCaptureSettings();
            return Normalize(settings);
        }
        catch
        {
            return Normalize(new ShadowCaptureSettings());
        }
    }

    public void Save(ShadowCaptureSettings settings)
    {
        settings = Normalize(settings);
        Directory.CreateDirectory(settings.SaveFolder);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOptions));
    }

    private static ShadowCaptureSettings Normalize(ShadowCaptureSettings settings)
    {
        settings.ReplaySeconds = Math.Clamp(settings.ReplaySeconds, 5, 900);
        settings.MaxGpuBudgetPercent = Math.Clamp(settings.MaxGpuBudgetPercent, 1, 25);
        settings.MaxFpsLoss = Math.Clamp(settings.MaxFpsLoss, 1, 30);
        settings.BitrateMbps = Math.Clamp(settings.BitrateMbps, 4, 100);
        settings.TargetFps = settings.TargetFps >= 55 ? 60 : 30;
        settings.MaxLibraryGb = Math.Clamp(settings.MaxLibraryGb, 5, 2048);
        if (string.IsNullOrWhiteSpace(settings.SaveFolder))
        {
            settings.SaveFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "D7 Clips");
        }
        if (string.IsNullOrWhiteSpace(settings.PreferredEncoder)) settings.PreferredEncoder = "Auto";
        if (string.IsNullOrWhiteSpace(settings.SaveHotkey)) settings.SaveHotkey = "F8";
        return settings;
    }
}
