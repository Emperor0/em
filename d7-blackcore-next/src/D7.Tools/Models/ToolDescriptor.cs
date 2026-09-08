namespace D7.Tools.Models;

public sealed record ToolDescriptor(
    string Id,
    string Name,
    string Vendor,
    string Version,
    string AssetName,
    Uri DownloadUri,
    string Sha256,
    string Integration,
    string License);

public sealed record ToolReadyResult(
    ToolDescriptor Tool,
    bool Ready,
    string? ExecutablePath,
    string MessageAr,
    bool Downloaded,
    bool Repaired);
