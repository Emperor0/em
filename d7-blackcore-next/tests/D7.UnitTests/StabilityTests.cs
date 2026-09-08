using D7.Stability;
using D7.Stability.Models;
using Xunit;

namespace D7.UnitTests;

public sealed class StabilityTests
{
    [Theory]
    [InlineData("Microsoft-Windows-WHEA-Logger", 18, "WHEA", "Critical")]
    [InlineData("Microsoft-Windows-WHEA-Logger", 19, "WHEA", "Warning")]
    [InlineData("Microsoft-Windows-WHEA-Logger", 46, "WHEA", "Critical")]
    [InlineData("Display", 4101, "GPU_DRIVER_RESET", "Critical")]
    [InlineData("Microsoft-Windows-Kernel-Power", 41, "UNEXPECTED_POWER", "Critical")]
    [InlineData("EventLog", 6008, "UNEXPECTED_SHUTDOWN", "Critical")]
    public void EventClassification_MapsKnownStabilitySignals(
        string provider,
        int eventId,
        string category,
        string severity)
    {
        var result = EventLogStabilityProbe.Classify(provider, eventId);
        Assert.Equal(category, result.Category);
        Assert.Equal(severity, result.Severity);
    }

    [Fact]
    public void StabilityComparer_ReturnsOnlyNewRecords()
    {
        var before = new StabilitySnapshot(
            true,
            DateTimeOffset.UtcNow,
            [new StabilityIssue(10, DateTimeOffset.UtcNow, "Display", 4101, "GPU_DRIVER_RESET", "Critical")]);
        var after = new StabilitySnapshot(
            true,
            DateTimeOffset.UtcNow,
            [
                new StabilityIssue(10, DateTimeOffset.UtcNow, "Display", 4101, "GPU_DRIVER_RESET", "Critical"),
                new StabilityIssue(11, DateTimeOffset.UtcNow, "Microsoft-Windows-WHEA-Logger", 19, "WHEA", "Warning"),
                new StabilityIssue(12, DateTimeOffset.UtcNow, "Microsoft-Windows-WHEA-Logger", 18, "WHEA", "Critical")
            ]);

        var delta = StabilityComparer.Compare(before, after);

        Assert.True(delta.Available);
        Assert.Equal(2, delta.NewIssueCount);
        Assert.Equal(1, delta.NewCriticalCount);
        Assert.DoesNotContain(delta.NewIssues, x => x.RecordId == 10);
    }

    [Fact]
    public void StabilityComparer_IsUnavailable_WhenEitherProbeFailed()
    {
        var unavailable = new StabilitySnapshot(false, DateTimeOffset.UtcNow, [], "denied");
        var available = new StabilitySnapshot(true, DateTimeOffset.UtcNow, []);

        var delta = StabilityComparer.Compare(unavailable, available);

        Assert.False(delta.Available);
        Assert.Empty(delta.NewIssues);
    }

    [Fact]
    public async Task WindowsEventProbe_ReturnsSnapshotWithoutThrowing()
    {
        if (!OperatingSystem.IsWindows()) return;

        var probe = new EventLogStabilityProbe();
        var snapshot = await probe.CaptureAsync(CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.NotNull(snapshot.Issues);
        if (!snapshot.Available)
            Assert.False(string.IsNullOrWhiteSpace(snapshot.Error));
    }
}
