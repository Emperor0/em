using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;
using D7.Stability.Models;

namespace D7.Stability;

[SupportedOSPlatform("windows")]
public sealed class EventLogStabilityProbe : IStabilityProbe
{
    private const string Query = "*[System[((Provider[@Name='Microsoft-Windows-WHEA-Logger'] and (EventID=18 or EventID=19 or EventID=20 or EventID=46)) or (Provider[@Name='Display'] and EventID=4101) or (Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41) or (Provider[@Name='EventLog'] and EventID=6008)) and TimeCreated[timediff(@SystemTime) <= 600000]]]";

    public Task<StabilitySnapshot> CaptureAsync(CancellationToken cancellationToken) =>
        Task.Run(() => Capture(cancellationToken), cancellationToken);

    private static StabilitySnapshot Capture(CancellationToken cancellationToken)
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var issues = new List<StabilityIssue>();
        try
        {
            var query = new EventLogQuery("System", PathType.LogName, Query)
            {
                ReverseDirection = true,
                TolerateQueryErrors = true
            };

            using var reader = new EventLogReader(query);
            while (issues.Count < 128)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var record = reader.ReadEvent();
                if (record is null) break;

                var provider = record.ProviderName ?? "Unknown";
                var eventId = record.Id;
                var time = record.TimeCreated is null ? null : new DateTimeOffset(record.TimeCreated.Value);
                var recordId = record.RecordId ?? HashCode.Combine(provider, eventId, time?.UtcTicks ?? 0L);
                var (category, severity) = Classify(provider, eventId);
                issues.Add(new StabilityIssue(recordId, time, provider, eventId, category, severity));
            }

            return new StabilitySnapshot(true, capturedAt, issues);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new StabilitySnapshot(false, capturedAt, issues, ex.Message);
        }
    }

    public static (string Category, string Severity) Classify(string provider, int eventId)
    {
        if (provider.Equals("Microsoft-Windows-WHEA-Logger", StringComparison.OrdinalIgnoreCase))
            return ("WHEA", eventId == 19 ? "Warning" : "Critical");
        if (provider.Equals("Display", StringComparison.OrdinalIgnoreCase) && eventId == 4101)
            return ("GPU_DRIVER_RESET", "Critical");
        if (provider.Equals("Microsoft-Windows-Kernel-Power", StringComparison.OrdinalIgnoreCase) && eventId == 41)
            return ("UNEXPECTED_POWER", "Critical");
        if (provider.Equals("EventLog", StringComparison.OrdinalIgnoreCase) && eventId == 6008)
            return ("UNEXPECTED_SHUTDOWN", "Critical");
        return ("OTHER", "Warning");
    }
}
