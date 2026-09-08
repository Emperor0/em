namespace D7.Games.Detection;

public sealed record GameProcessFacts(
    int ProcessId,
    string ProcessName,
    string? ExecutablePath,
    bool IsForeground,
    bool HasGraphicsActivity,
    bool IsLearned,
    bool IsKnownGameInstallPath = false);

public sealed record GameDetectionDecision(
    bool IsGame,
    int Score,
    string ConfidenceAr,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Rejections);
