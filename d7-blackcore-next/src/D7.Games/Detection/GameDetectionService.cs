using D7.Games.Profiles;

namespace D7.Games.Detection;

public sealed record ActiveGame(
    int ProcessId,
    string ProcessName,
    string? ExecutablePath,
    int Score,
    string ConfidenceAr,
    IReadOnlyList<string> Evidence);

public sealed class GameDetectionService
{
    private static readonly HashSet<string> KnownGameProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "007FirstLight",
        "cod",
        "cod22",
        "ModernWarfare",
        "ModernWarfareLauncher"
    };

    private readonly ForegroundProcessReader _foreground;
    private readonly GameProcessClassifier _classifier;
    private readonly GameProfileStore _profiles;

    public GameDetectionService(
        ForegroundProcessReader foreground,
        GameProcessClassifier classifier,
        GameProfileStore profiles)
    {
        _foreground = foreground;
        _classifier = classifier;
        _profiles = profiles;
    }

    public async Task<ActiveGame?> DetectActiveGameAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var foreground = _foreground.TryGetForegroundProcess();
        if (foreground is null) return null;

        var learned = await _profiles.LoadAsync(cancellationToken).ConfigureAwait(false);
        var isLearned = learned.Any(x => GameProfileStore.Matches(x, foreground.ProcessName, foreground.ExecutablePath));
        var known = IsKnownGame(foreground.ProcessName, foreground.ExecutablePath);

        var decision = _classifier.Classify(new GameProcessFacts(
            foreground.ProcessId,
            foreground.ProcessName,
            foreground.ExecutablePath,
            IsForeground: true,
            HasGraphicsActivity: false,
            IsLearned: isLearned,
            IsKnownGameInstallPath: known));

        if (!decision.IsGame) return null;

        var now = DateTimeOffset.UtcNow;
        var existing = learned.FirstOrDefault(x => GameProfileStore.Matches(x, foreground.ProcessName, foreground.ExecutablePath));
        var profile = existing is null
            ? new LearnedGameProfile(foreground.ProcessName, foreground.ExecutablePath, known ? "catalog" : "detected", now, now)
            : existing with { ExecutablePath = foreground.ExecutablePath ?? existing.ExecutablePath, LastSeenAt = now };
        await _profiles.UpsertAsync(profile, cancellationToken).ConfigureAwait(false);

        return new ActiveGame(
            foreground.ProcessId,
            foreground.ProcessName,
            foreground.ExecutablePath,
            decision.Score,
            decision.ConfidenceAr,
            decision.Evidence);
    }

    public static bool IsKnownGame(string processName, string? executablePath)
    {
        var normalized = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;
        if (KnownGameProcessNames.Contains(normalized)) return true;

        var path = executablePath ?? string.Empty;
        return path.Contains(@"\steamapps\common\", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\XboxGames\", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\EA Games\", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\Games\Call of Duty\", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\GM\Call of Duty\", StringComparison.OrdinalIgnoreCase);
    }
}
