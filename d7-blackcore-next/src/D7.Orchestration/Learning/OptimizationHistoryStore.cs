using System.Text.Json;
using D7.Core.Foundation;
using D7.Games.Detection;

namespace D7.Orchestration.Learning;

public enum OptimizationHistoryOutcome
{
    Kept,
    RolledBack,
    Inconclusive,
    Failed,
    NoOp
}

public sealed record OptimizationHistoryRecord(
    string ProcessName,
    string? ExecutablePath,
    string OperationId,
    string OperationNameAr,
    OptimizationHistoryOutcome Outcome,
    DateTimeOffset RecordedAt,
    double? OnePercentLowDeltaPercent,
    double? P99DeltaPercent,
    int? StutterDelta,
    string MessageAr,
    IReadOnlyList<string> Reasons);

public sealed class OptimizationHistoryStore
{
    private const int MaxRecords = 1000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public OptimizationHistoryStore(AppPaths paths)
    {
        Directory.CreateDirectory(paths.Profiles);
        _path = Path.Combine(paths.Profiles, "optimization-history.v1.json");
    }

    public async Task<IReadOnlyList<OptimizationHistoryRecord>> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<OptimizationHistoryRecord>> GetForGameAsync(
        ActiveGame game,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        var records = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return records
            .Where(x => MatchesGame(x, game))
            .OrderByDescending(x => x.RecordedAt)
            .ToArray();
    }

    public async Task AppendAsync(OptimizationHistoryRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false);
            var updated = existing
                .Append(record)
                .OrderByDescending(x => x.RecordedAt)
                .Take(MaxRecords)
                .OrderBy(x => x.RecordedAt)
                .ToArray();

            var temp = _path + ".tmp";
            await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                await JsonSerializer.SerializeAsync(stream, updated, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temp, _path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static bool MatchesGame(OptimizationHistoryRecord record, ActiveGame game)
    {
        if (!string.Equals(Normalize(record.ProcessName), Normalize(game.ProcessName), StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrWhiteSpace(record.ExecutablePath) || string.IsNullOrWhiteSpace(game.ExecutablePath))
            return true;

        try
        {
            return string.Equals(
                Path.GetFullPath(record.ExecutablePath),
                Path.GetFullPath(game.ExecutablePath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(record.ExecutablePath, game.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task<OptimizationHistoryRecord[]> ReadUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return Array.Empty<OptimizationHistoryRecord>();

        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            return await JsonSerializer.DeserializeAsync<OptimizationHistoryRecord[]>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                ?? Array.Empty<OptimizationHistoryRecord>();
        }
        catch (JsonException)
        {
            return Array.Empty<OptimizationHistoryRecord>();
        }
        catch (IOException)
        {
            return Array.Empty<OptimizationHistoryRecord>();
        }
    }

    private static string Normalize(string processName) =>
        processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4].Trim()
            : processName.Trim();
}
