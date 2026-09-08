using D7.Stability.Models;

namespace D7.Stability;

public sealed record StabilityDelta(
    bool Available,
    int NewIssueCount,
    int NewCriticalCount,
    IReadOnlyList<StabilityIssue> NewIssues);

public static class StabilityComparer
{
    public static StabilityDelta Compare(StabilitySnapshot before, StabilitySnapshot after)
    {
        if (!before.Available || !after.Available)
            return new StabilityDelta(false, 0, 0, Array.Empty<StabilityIssue>());

        var beforeIds = before.Issues.Select(x => x.RecordId).ToHashSet();
        var added = after.Issues.Where(x => !beforeIds.Contains(x.RecordId)).ToArray();
        var critical = added.Count(x => string.Equals(x.Severity, "Critical", StringComparison.OrdinalIgnoreCase));
        return new StabilityDelta(true, added.Length, critical, added);
    }
}
