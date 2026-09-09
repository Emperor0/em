namespace D7.Updater.Models;

public sealed record UpdateManifest(
    string Schema,
    string Product,
    string Channel,
    string Version,
    int MinimumWindowsBuild,
    Uri PackageUrl,
    string Sha256,
    string PackageType,
    string EntryExecutable,
    DateTimeOffset PublishedUtc);

public sealed record UpdateManifestValidation(
    bool Valid,
    IReadOnlyList<string> Errors);

public sealed record StagedUpdate(
    UpdateManifest Manifest,
    string PackagePath,
    string PayloadDirectory,
    string EntryExecutablePath,
    string PendingManifestPath,
    bool Downloaded,
    bool ReusedVerifiedPackage);

public sealed record UpdateStageResult(
    bool Success,
    StagedUpdate? Staged,
    string MessageAr,
    string? TechnicalError = null);
