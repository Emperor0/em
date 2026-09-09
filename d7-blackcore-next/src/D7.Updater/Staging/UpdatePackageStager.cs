using System.Security.Cryptography;
using System.Text.Json;
using D7.Core.Foundation;
using D7.Core.Logging;
using D7.Updater.Models;
using D7.Updater.Security;
using D7.Updater.Validation;

namespace D7.Updater.Staging;

public sealed class UpdatePackageStager
{
    private const long MaxPackageBytes = 1024L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly AppPaths _paths;
    private readonly JsonLineLogger _logger;
    private readonly HttpClient _httpClient;

    public UpdatePackageStager(AppPaths paths, JsonLineLogger logger, HttpClient httpClient)
    {
        _paths = paths;
        _logger = logger;
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    public async Task<UpdateStageResult> StageAsync(UpdateManifest manifest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var validation = UpdateManifestValidator.Validate(manifest, Environment.OSVersion.Version.Build);
        if (!validation.Valid)
        {
            return new UpdateStageResult(
                false,
                null,
                "تم رفض ملف التحديث لأنه لم يجتز التحقق الأمني.",
                string.Join(" | ", validation.Errors));
        }

        var operationId = $"UPDATE-STAGE-{manifest.Version}";
        var safeVersion = MakeSafeDirectoryName(manifest.Version);
        var hashPrefix = manifest.Sha256[..12].ToLowerInvariant();
        var stageRoot = Path.Combine(_paths.Updates, safeVersion, hashPrefix);
        var packagePath = Path.Combine(stageRoot, "package.zip");
        var packageTemp = packagePath + ".part";
        var payloadPath = Path.Combine(stageRoot, "payload");
        var verifiedMarker = Path.Combine(payloadPath, ".d7-verified-sha256");
        var pendingPath = Path.Combine(stageRoot, "pending-update.json");
        Directory.CreateDirectory(stageRoot);

        var downloaded = false;
        var reusedVerifiedPackage = false;
        string? extractionTemp = null;

        try
        {
            if (File.Exists(packagePath) && await VerifySha256Async(packagePath, manifest.Sha256, cancellationToken).ConfigureAwait(false))
            {
                reusedVerifiedPackage = true;
            }
            else
            {
                TryDeleteFile(packagePath);
                TryDeleteFile(packageTemp);
                await DownloadPackageAsync(manifest.PackageUrl, packageTemp, cancellationToken).ConfigureAwait(false);
                downloaded = true;

                if (!await VerifySha256Async(packageTemp, manifest.Sha256, cancellationToken).ConfigureAwait(false))
                {
                    TryDeleteFile(packageTemp);
                    await _logger.WriteAsync(
                        "Updater",
                        operationId,
                        "Error",
                        "تم حذف حزمة تحديث فشلت في التحقق من SHA-256.",
                        new { manifest.Version, manifest.PackageUrl, manifest.Sha256 },
                        CancellationToken.None);
                    return new UpdateStageResult(false, null, "فشل التحقق من سلامة حزمة التحديث وتم حذفها.");
                }

                File.Move(packageTemp, packagePath, true);
            }

            var expectedEntry = Path.GetFullPath(Path.Combine(payloadPath, manifest.EntryExecutable.Replace('/', Path.DirectorySeparatorChar)));
            var reusablePayload = File.Exists(expectedEntry) && File.Exists(verifiedMarker) &&
                                  string.Equals((await File.ReadAllTextAsync(verifiedMarker, cancellationToken).ConfigureAwait(false)).Trim(), manifest.Sha256, StringComparison.OrdinalIgnoreCase);

            if (!reusablePayload)
            {
                extractionTemp = payloadPath + ".tmp." + Guid.NewGuid().ToString("N");
                Directory.CreateDirectory(extractionTemp);
                SafeZipExtractor.Extract(packagePath, extractionTemp);

                var extractedEntry = Path.GetFullPath(Path.Combine(extractionTemp, manifest.EntryExecutable.Replace('/', Path.DirectorySeparatorChar)));
                var tempRoot = Path.GetFullPath(extractionTemp) + Path.DirectorySeparatorChar;
                if (!extractedEntry.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(extractedEntry))
                    throw new InvalidDataException("The update package does not contain the declared entry executable.");

                await File.WriteAllTextAsync(
                    Path.Combine(extractionTemp, ".d7-verified-sha256"),
                    manifest.Sha256.ToLowerInvariant(),
                    cancellationToken).ConfigureAwait(false);

                TryDeleteDirectory(payloadPath);
                Directory.Move(extractionTemp, payloadPath);
                extractionTemp = null;
            }

            var entryExecutablePath = Path.GetFullPath(Path.Combine(payloadPath, manifest.EntryExecutable.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(entryExecutablePath))
                throw new FileNotFoundException("Declared update entry executable is missing after extraction.", entryExecutablePath);

            var staged = new StagedUpdate(
                manifest,
                packagePath,
                payloadPath,
                entryExecutablePath,
                pendingPath,
                downloaded,
                reusedVerifiedPackage);

            await WritePendingAsync(pendingPath, staged, cancellationToken).ConfigureAwait(false);
            await _logger.WriteAsync(
                "Updater",
                operationId,
                "Information",
                "تم تجهيز حزمة التحديث والتحقق منها دون تفعيلها.",
                new { manifest.Version, manifest.Channel, packagePath, payloadPath, downloaded, reusedVerifiedPackage },
                CancellationToken.None);

            return new UpdateStageResult(true, staged, "تم تجهيز التحديث والتحقق منه. لم يتم استبدال النسخة الحالية بعد.");
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(packageTemp);
            if (!string.IsNullOrWhiteSpace(extractionTemp)) TryDeleteDirectory(extractionTemp);
            throw;
        }
        catch (Exception ex)
        {
            TryDeleteFile(packageTemp);
            if (!string.IsNullOrWhiteSpace(extractionTemp)) TryDeleteDirectory(extractionTemp);
            TryDeleteFile(pendingPath);
            await _logger.WriteAsync(
                "Updater",
                operationId,
                "Error",
                "تعذر تجهيز التحديث ولم يتم تفعيل أي ملفات.",
                new { manifest.Version, error = ex.Message },
                CancellationToken.None);
            return new UpdateStageResult(false, null, "تعذر تجهيز التحديث بأمان. بقيت النسخة الحالية دون تغيير.", ex.Message);
        }
    }

    public static async Task<bool> VerifySha256Async(string filePath, string expectedSha256, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath) || expectedSha256.Length != 64) return false;
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        var actual = Convert.ToHexString(hash).ToLowerInvariant();
        var expected = expectedSha256.ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(actual),
            System.Text.Encoding.ASCII.GetBytes(expected));
    }

    private async Task DownloadPackageAsync(Uri uri, string destinationPath, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is long length && length > MaxPackageBytes)
            throw new InvalidDataException("Update package exceeds the maximum allowed download size.");

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            total = checked(total + read);
            if (total > MaxPackageBytes)
                throw new InvalidDataException("Update package exceeded the maximum allowed size while downloading.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WritePendingAsync(string pendingPath, StagedUpdate staged, CancellationToken cancellationToken)
    {
        var temp = pendingPath + ".tmp";
        TryDeleteFile(temp);
        await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, staged, JsonOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        File.Move(temp, pendingPath, true);
    }

    private static string MakeSafeDirectoryName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
