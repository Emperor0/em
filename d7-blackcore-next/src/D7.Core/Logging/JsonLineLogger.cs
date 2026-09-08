using System.Text.Json;
using D7.Core.Foundation;

namespace D7.Core.Logging;

public sealed class JsonLineLogger
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly string _path;

    public JsonLineLogger(AppPaths paths)
    {
        Directory.CreateDirectory(paths.Logs);
        _path = Path.Combine(paths.Logs, $"d7-{DateTime.UtcNow:yyyyMMdd}.jsonl");
    }

    public async Task WriteAsync(
        string module,
        string operationId,
        string level,
        string message,
        object? data = null,
        CancellationToken cancellationToken = default)
    {
        var entry = new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            module,
            operationId,
            level,
            message,
            data
        };

        var json = JsonSerializer.Serialize(entry);
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(_path, json + Environment.NewLine, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }
}
