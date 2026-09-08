using D7.Benchmark.Models;

namespace D7.Benchmark.Capture;

public interface IFrameCaptureService
{
    Task<BenchmarkCaptureResult> CaptureAsync(int processId, TimeSpan duration, CancellationToken cancellationToken);
}
