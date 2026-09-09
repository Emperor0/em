using System.Text.Json;
using System.Text.RegularExpressions;
using D7.Updater.Validation;

namespace D7.Updater.Activation;

public sealed record VersionPointer(
    string Schema,
    string Version,
    string RelativeExecutable,
    string PackageSha256,
    DateTimeOffset ActivatedUtc);

public sealed class VersionPointerStore
{
    private static readonly Regex Sha256Regex = new("^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _installRoot;
    private readonly string _pointerPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public VersionPointerStore(string installRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        _installRoot = Path.GetFullPath(installRoot);
        _pointerPath = Path.Combine(_installRoot, "current.json");
    }

    public string PointerPath => _pointerPath;

    public async Task<VersionPointer?> ReadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_pointerPath)) return null;
            await using var stream = File.OpenRead(_pointerPath);
            var pointer = await JsonSerializer.DeserializeAsync<VersionPointer>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (pointer is null) return null;
            Validate(pointer);
            _ = ResolveExecutablePath(pointer);
            return pointer;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteAtomicAsync(VersionPointer pointer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pointer);
        Validate(pointer);
        var executable = ResolveExecutablePath(pointer);
        if (!File.Exists(executable))
            throw new FileNotFoundException("Version pointer executable does not exist.", executable);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_installRoot);
            var temp = _pointerPath + ".tmp." + Guid.NewGuid().ToString("N");
            try
            {
                await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    await JsonSerializer.SerializeAsync(stream, pointer, JsonOptions, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                File.Move(temp, _pointerPath, true);
            }
            finally
            {
                TryDelete(temp);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public string ResolveExecutablePath(VersionPointer pointer)
    {
        Validate(pointer);
        var resolved = Path.GetFullPath(Path.Combine(_installRoot, pointer.RelativeExecutable.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = _installRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? _installRoot
            : _installRoot + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Version pointer escaped the install directory.");
        return resolved;
    }

    private static void Validate(VersionPointer pointer)
    {
        if (!string.Equals(pointer.Schema, "D7.VersionPointer.v1", StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported version pointer schema.");
        if (string.IsNullOrWhiteSpace(pointer.Version))
            throw new InvalidDataException("Version pointer is missing a version.");
        if (!UpdateManifestValidator.IsSafeRelativePath(pointer.RelativeExecutable) ||
            !pointer.RelativeExecutable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsafe version pointer executable path.");
        if (!Sha256Regex.IsMatch(pointer.PackageSha256 ?? string.Empty))
            throw new InvalidDataException("Invalid package SHA-256 in version pointer.");
        if (pointer.ActivatedUtc == default)
            throw new InvalidDataException("Invalid activation timestamp.");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }
}
