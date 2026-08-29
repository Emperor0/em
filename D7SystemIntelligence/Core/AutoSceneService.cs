using System.Text.Json;

namespace D7SystemIntelligence.Core;

public sealed class AutoSceneSettings
{
    public bool Enabled { get; set; }
    public int StabilityDelaySeconds { get; set; } = 8;
    public List<string> CompetitiveGameTokens { get; set; } =
    [
        "cod", "cod26", "valorant", "cs2", "counter-strike", "fortnite", "apex", "overwatch", "rainbowsix", "r6"
    ];
}

public sealed class AutoSceneSettingsStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public AutoSceneSettingsStore()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D7SystemIntelligence", "Policies");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "auto-scene.json");
    }

    public AutoSceneSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return Normalize(new());
            return Normalize(JsonSerializer.Deserialize<AutoSceneSettings>(File.ReadAllText(_path), Options) ?? new());
        }
        catch { return Normalize(new()); }
    }

    public void Save(AutoSceneSettings settings)
    {
        settings = Normalize(settings);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
    }

    private static AutoSceneSettings Normalize(AutoSceneSettings settings)
    {
        settings.StabilityDelaySeconds = Math.Clamp(settings.StabilityDelaySeconds, 3, 30);
        settings.CompetitiveGameTokens ??= [];
        settings.CompetitiveGameTokens = settings.CompetitiveGameTokens
            .Select(x => (x ?? string.Empty).Trim().ToLowerInvariant())
            .Where(x => x.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return settings;
    }
}

public sealed class AutoSceneDirector
{
    private readonly AutoSceneSettingsStore _store;
    private D7Mission _candidate;
    private string? _candidateGame;
    private DateTimeOffset _candidateSince;

    public AutoSceneDirector(AutoSceneSettingsStore store) => _store = store;

    public AutoSceneSettings Settings => _store.Load();

    public void Save(AutoSceneSettings settings) => _store.Save(settings);

    public (bool Ready, D7Mission Target, string Reason) Evaluate(RuntimeContext? context, D7Mission current)
    {
        var settings = _store.Load();
        if (!settings.Enabled)
        {
            ResetCandidate();
            return (false, current, "Auto Scene متوقف.");
        }

        var desired = Decide(context, settings);
        var game = context?.PrimaryGame;
        if (desired == current)
        {
            ResetCandidate();
            return (false, desired, $"Auto Scene مستقر على {D7MissionEngine.MissionArabic(desired)}.");
        }

        if (desired == D7Mission.None)
        {
            ResetCandidate();
            return (current != D7Mission.None, D7Mission.None, "انتهت اللعبة؛ Auto Scene سيستعيد إعدادات الجلسة.");
        }

        if (_candidate != desired || !string.Equals(_candidateGame, game, StringComparison.OrdinalIgnoreCase))
        {
            _candidate = desired;
            _candidateGame = game;
            _candidateSince = DateTimeOffset.Now;
            return (false, desired, $"تم اكتشاف {game ?? "جلسة"}. ينتظر D7 {settings.StabilityDelaySeconds} ثوانٍ قبل تطبيق المهمة لتجنب تغيير النظام أثناء الإقلاع.");
        }

        var elapsed = (DateTimeOffset.Now - _candidateSince).TotalSeconds;
        if (elapsed < settings.StabilityDelaySeconds)
            return (false, desired, $"تثبيت المشهد… {Math.Max(0, settings.StabilityDelaySeconds - elapsed):0} ث.");

        ResetCandidate();
        return (true, desired, $"المشهد ثابت؛ تطبيق {D7MissionEngine.MissionArabic(desired)}.");
    }

    private static D7Mission Decide(RuntimeContext? context, AutoSceneSettings settings)
    {
        if (context == null || string.IsNullOrWhiteSpace(context.PrimaryGame)) return D7Mission.None;
        var competitive = settings.CompetitiveGameTokens.Any(token => context.PrimaryGame.Contains(token, StringComparison.OrdinalIgnoreCase));
        if (context.Mode == D7RuntimeMode.StreamGaming) return D7Mission.StreamRanked;
        if (context.Mode == D7RuntimeMode.Gaming) return competitive ? D7Mission.ProRanked : D7Mission.Story;
        return D7Mission.None;
    }

    private void ResetCandidate()
    {
        _candidate = D7Mission.None;
        _candidateGame = null;
        _candidateSince = default;
    }
}
