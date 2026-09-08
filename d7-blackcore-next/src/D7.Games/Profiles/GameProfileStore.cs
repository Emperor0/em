using System.Text.Json;
using D7.Core.Foundation;

namespace D7.Games.Profiles;

public sealed record LearnedGameProfile(
    string ProcessName,
    string? ExecutablePath,
    string Source,
    DateTimeOffset AddedAt,
    DateTimeOffset LastSeenAt);

public sealed class GameProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public GameProfileStore(AppPaths paths)
    {
        Directory.CreateDirectory(paths.Profiles);
        _path = Path.Combine(paths.Profiles, "learned-games.v1.json");
    }

    public async Task<IReadOnlyList<LearnedGameProfile>> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path)) return Array.Empty<LearnedGameProfile>();
            await using var stream = File.OpenRead(_path);
            var profiles = await JsonSerializer.DeserializeAsync<LearnedGameProfile[]>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            return profiles ?? Array.Empty<LearnedGameProfile>();
        }
        catch (JsonException)
        {
            return Array.Empty<LearnedGameProfile>();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertAsync(LearnedGameProfile profile, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LearnedGameProfile[] existing;
            if (File.Exists(_path))
            {
                try
                {
                    await using var input = File.OpenRead(_path);
                    existing = await JsonSerializer.DeserializeAsync<LearnedGameProfile[]>(input, JsonOptions, cancellationToken).ConfigureAwait(false)
                        ?? Array.Empty<LearnedGameProfile>();
                }
                catch (JsonException)
                {
                    existing = Array.Empty<LearnedGameProfile>();
                }
            }
            else
            {
                existing = Array.Empty<LearnedGameProfile>();
            }

            var normalized = Normalize(profile.ProcessName);
            var updated = existing
                .Where(x => !string.Equals(Normalize(x.ProcessName), normalized, StringComparison.OrdinalIgnoreCase))
                .Append(profile)
                .OrderBy(x => x.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var temp = _path + ".tmp";
            await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                await JsonSerializer.SerializeAsync(output, updated, JsonOptions, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temp, _path, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static bool Matches(LearnedGameProfile profile, string processName, string? executablePath)
    {
        if (!string.Equals(Normalize(profile.ProcessName), Normalize(processName), StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(profile.ExecutablePath) || string.IsNullOrWhiteSpace(executablePath)) return true;
        return string.Equals(Path.GetFullPath(profile.ExecutablePath), Path.GetFullPath(executablePath), StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value) =>
        value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? value[..^4].Trim() : value.Trim();
}
