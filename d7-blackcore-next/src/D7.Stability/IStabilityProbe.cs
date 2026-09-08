using D7.Stability.Models;

namespace D7.Stability;

public interface IStabilityProbe
{
    Task<StabilitySnapshot> CaptureAsync(CancellationToken cancellationToken);
}

public sealed class NullStabilityProbe : IStabilityProbe
{
    public Task<StabilitySnapshot> CaptureAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new StabilitySnapshot(false, DateTimeOffset.UtcNow, Array.Empty<StabilityIssue>(), "Stability probe unavailable."));
}
