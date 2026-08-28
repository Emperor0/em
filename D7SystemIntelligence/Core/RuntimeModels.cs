namespace D7SystemIntelligence.Core;

public enum D7RuntimeMode
{
    Idle,
    Desktop,
    Gaming,
    Streaming,
    StreamGaming,
    Maintenance
}

public enum D7Profile
{
    Safe,
    Balanced,
    MaxPerformance
}

public sealed record RuntimeContext(
    D7RuntimeMode Mode,
    string? PrimaryGame,
    bool ObsRunning,
    bool TikTokRunning,
    DateTimeOffset ObservedAt,
    string Reason);

public sealed record PolicyDecision(
    string Severity,
    string Area,
    string Title,
    string Detail,
    bool AutoSafe = false);

public sealed record OrchestratorStatus(
    RuntimeContext Context,
    D7Profile Profile,
    IReadOnlyList<PolicyDecision> Decisions,
    string Summary);

public sealed record TelemetrySample(
    DateTimeOffset At,
    D7RuntimeMode Mode,
    string? Game,
    D7Profile Profile,
    float CpuLoad,
    float GpuLoad,
    float CpuTemp,
    float GpuTemp,
    float RamLoad,
    float? VramLoad,
    int FanCount,
    int ControllableFanCount);
