namespace D7.Stability.Models;

public sealed record StabilityIssue(
    long RecordId,
    DateTimeOffset? TimeCreated,
    string Provider,
    int EventId,
    string Category,
    string Severity);

public sealed record StabilitySnapshot(
    bool Available,
    DateTimeOffset CapturedAt,
    IReadOnlyList<StabilityIssue> Issues,
    string? Error = null)
{
    public int CriticalCount => Issues.Count(x => string.Equals(x.Severity, "Critical", StringComparison.OrdinalIgnoreCase));
}
