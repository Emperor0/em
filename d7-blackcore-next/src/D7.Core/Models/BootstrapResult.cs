namespace D7.Core.Models;

public sealed record BootstrapCheck(
    string Id,
    string TitleAr,
    bool Passed,
    bool Blocking,
    string DetailAr);

public sealed record BootstrapResult(
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    bool SafeMode,
    IReadOnlyList<BootstrapCheck> Checks)
{
    public bool Ready => Checks.All(x => x.Passed || !x.Blocking);
}
