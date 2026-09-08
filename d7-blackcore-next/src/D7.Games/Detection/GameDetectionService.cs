using D7.Games.Profiles;

namespace D7.Games.Detection;

public sealed record ActiveGame(
    int ProcessId,
    string ProcessName,
    string? ExecutablePath,
    int Score,
    string ConfidenceAr,
    IReadOnlyList<string> Evidence);

public sealed class GameDetectionService : IActiveGameDetector
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
    private readonly RunningProcessReader _running;
    private readonly GameProcessClassifier _classifier;
    private readonly GameProfileStore _profiles;

    public GameDetectionService(
        ForegroundProcessReader foreground,
        RunningProcessReader running,
        GameProcessClassifier classifier,
        GameProfileStore profiles)
    {
        _foreground = foreground;
        _running = running;
        _classifier = classifier;
        _profiles = profiles;
    }

    public async Task<ActiveGame?> DetectActiveGameAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var learned = await _profiles.LoadAsync(cancellationToken).ConfigureAwait(false);
        var foreground = _foreground.TryGetForegroundProcess();
        var foregroundPid = foreground?.ProcessId;

        var candidates = new Dictionary<int, RunningProcessInfo>();
        if (foreground is not null)
            candidates[foreground.ProcessId] = new RunningProcessInfo(foreground.ProcessId, foreground.ProcessName, foreground.ExecutablePath);

        foreach (var process in _running.Snapshot())
            candidates.TryAdd(process.ProcessId, process);

        Candidate? best = null;
        foreach (var process in candidates.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isLearned = learned.Any(x => GameProfileStore.Matches(x, process.ProcessName, process.ExecutablePath));
            var knownExecutable = IsKnownExecutableName(process.ProcessName);
            var knownInstallPath = IsKnownGameInstallPath(process.ExecutablePath);
            var decision = _classifier.Classify(new GameProcessFacts(
                process.ProcessId,
                process.ProcessName,
                process.ExecutablePath,
                IsForeground: process.ProcessId == foregroundPid,
                HasGraphicsActivity: false,
                IsLearned: isLearned,
                IsKnownGameInstallPath: knownInstallPath,
                IsKnownExecutable: knownExecutable));

            if (!decision.IsGame) continue;
            var candidate = new Candidate(process, decision, isLearned, knownExecutable, knownInstallPath);
            if (best is null || IsBetter(candidate, best, foregroundPid)) best = candidate;
        }

        if (best is null) return null;

        var now = DateTimeOffset.UtcNow;
        var existing = learned.FirstOrDefault(x => GameProfileStore.Matches(x, best.Process.ProcessName, best.Process.ExecutablePath));
        var source = best.IsLearned
            ? existing?.Source ?? "learned"
            : best.IsKnownExecutable
                ? "catalog"
                : best.IsKnownInstallPath
                    ? "library"
                    : "detected";
        var profile = existing is null
            ? new LearnedGameProfile(best.Process.ProcessName, best.Process.ExecutablePath, source, now, now)
            : existing with { ExecutablePath = best.Process.ExecutablePath ?? existing.ExecutablePath, LastSeenAt = now };
        await _profiles.UpsertAsync(profile, cancellationToken).ConfigureAwait(false);

        return new ActiveGame(
            best.Process.ProcessId,
            best.Process.ProcessName,
            best.Process.ExecutablePath,
            best.Decision.Score,
            best.Decision.ConfidenceAr,
            best.Decision.Evidence);
    }

    public static bool IsKnownGame(string processName, string? executablePath) =>
        IsKnownExecutableName(processName) || IsKnownGameInstallPath(executablePath);

    public static bool IsKnownExecutableName(string processName)
    {
        var normalized = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;
        return KnownGameProcessNames.Contains(normalized);
    }

    public static bool IsKnownGameInstallPath(string? executablePath)
    {
        var path = executablePath ?? string.Empty;
        return path.Contains(@"\steamapps\common\", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\XboxGames\", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\EA Games\", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\Games\Call of Duty\", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\GM\Call of Duty\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBetter(Candidate candidate, Candidate current, int? foregroundPid)
    {
        if (candidate.Decision.Score != current.Decision.Score)
            return candidate.Decision.Score > current.Decision.Score;
        var candidateForeground = candidate.Process.ProcessId == foregroundPid;
        var currentForeground = current.Process.ProcessId == foregroundPid;
        if (candidateForeground != currentForeground) return candidateForeground;
        if (candidate.IsLearned != current.IsLearned) return candidate.IsLearned;
        return candidate.Process.ProcessId < current.Process.ProcessId;
    }

    private sealed record Candidate(
        RunningProcessInfo Process,
        GameDetectionDecision Decision,
        bool IsLearned,
        bool IsKnownExecutable,
        bool IsKnownInstallPath);
}
