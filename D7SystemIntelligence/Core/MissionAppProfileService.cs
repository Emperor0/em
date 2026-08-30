namespace D7SystemIntelligence.Core;

public sealed record MissionAppProfileResult(
    int RunningApps,
    int VerifiedChanges,
    int Errors,
    string Detail);

public sealed class MissionAppProfileService
{
    private readonly AppIntelligenceService _apps = new();
    private readonly HashSet<ManagedAppId> _owned = [];

    private static readonly ManagedAppId[] ManagedIds =
    [
        ManagedAppId.Discord,
        ManagedAppId.Steam,
        ManagedAppId.NvidiaApp,
        ManagedAppId.Obs,
        ManagedAppId.TikTokLiveStudio,
        ManagedAppId.Chrome,
        ManagedAppId.Edge
    ];

    public async Task<MissionAppProfileResult> ApplyAsync(AppProfileMode mode, CancellationToken token = default)
    {
        var states = await _apps.ScanAsync(token);
        var running = states.Where(x => x.Running && ManagedIds.Contains(x.Id)).ToArray();
        if (running.Length == 0)
            return new(0, 0, 0, "لا توجد تطبيقات مدارة شغالة الآن تحتاج App Profile.");

        var verified = 0;
        var errors = 0;
        var messages = new List<string>();

        foreach (var app in running)
        {
            token.ThrowIfCancellationRequested();
            var detail = await _apps.ApplyProfileAsync(app.Id, mode, token);
            var changed = detail.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Count(x => x.Contains("[Verified]", StringComparison.OrdinalIgnoreCase));
            var failed = detail.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Count(x => x.Contains("تخطي", StringComparison.OrdinalIgnoreCase) ||
                            x.Contains("لم يثبت", StringComparison.OrdinalIgnoreCase));

            if (changed > 0)
            {
                _owned.Add(app.Id);
                verified += changed;
            }
            errors += failed;
            messages.Add(detail);
        }

        return new(running.Length, verified, errors, string.Join(Environment.NewLine + Environment.NewLine, messages));
    }

    public async Task<string> RestoreAsync(CancellationToken token = default)
    {
        if (_owned.Count == 0)
            return "لا توجد App Profile changes مملوكة لـD7KT تحتاج استعادة.";

        var messages = new List<string>();
        foreach (var id in _owned.ToArray())
        {
            token.ThrowIfCancellationRequested();
            messages.Add(await _apps.RestoreProfileAsync(id, silentWhenMissing: true, token));
        }
        _owned.Clear();
        return string.Join(Environment.NewLine, messages.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    public bool HasOwnedChanges => _owned.Count > 0;
}
