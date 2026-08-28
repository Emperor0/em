using System.Text.Json;

namespace D7SystemIntelligence.Core;

public sealed class TelemetryStore
{
    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastWrite = DateTimeOffset.MinValue;

    public TelemetryStore()
    {
        _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "D7SystemIntelligence",
            "Telemetry");
        Directory.CreateDirectory(_root);
    }

    public async Task AppendAsync(TelemetrySample sample, CancellationToken cancellationToken = default)
    {
        if (sample.At - _lastWrite < TimeSpan.FromSeconds(5)) return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (sample.At - _lastWrite < TimeSpan.FromSeconds(5)) return;
            var path = Path.Combine(_root, $"telemetry-{sample.At:yyyyMMdd}.jsonl");
            var json = JsonSerializer.Serialize(sample);
            await File.AppendAllTextAsync(path, json + Environment.NewLine, cancellationToken);
            _lastWrite = sample.At;
        }
        finally
        {
            _gate.Release();
        }
    }

    public string RootPath => _root;
}
