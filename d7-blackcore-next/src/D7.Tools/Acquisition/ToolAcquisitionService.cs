using System.Collections.Concurrent;
using System.Security.Cryptography;
using D7.Core.Foundation;
using D7.Core.Logging;
using D7.Tools.Models;
using D7.Tools.Security;

namespace D7.Tools.Acquisition;

public sealed class ToolAcquisitionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly AppPaths _paths;
    private readonly JsonLineLogger _logger;
    private readonly HttpClient _httpClient;

    public ToolAcquisitionService(AppPaths paths, JsonLineLogger logger, HttpClient httpClient)
    {
        _paths = paths;
        _logger = logger;
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromMinutes(3);
    }

    public async Task<ToolReadyResult> EnsurePortableAsync(ToolDescriptor tool, CancellationToken cancellationToken)
    {
        var gate = Gates.GetOrAdd(tool.Id, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.Combine(_paths.Tools, tool.Id, tool.Version);
            var target = Path.Combine(directory, tool.AssetName);
            Directory.CreateDirectory(directory);

            if (File.Exists(target))
            {
                var existingValidation = await ValidateAsync(target, tool, cancellationToken).ConfigureAwait(false);
                if (existingValidation.Accepted)
                {
                    await WriteAuditAsync(tool, target, existingValidation, "ExistingVerified", downloaded: false, cancellationToken).ConfigureAwait(false);
                    return new ToolReadyResult(tool, true, target, "الأداة جاهزة وتم التحقق من سلامتها.", false, false);
                }

                File.Delete(target);
                await WriteAuditAsync(tool, target, existingValidation, "ExistingRejected", downloaded: false, cancellationToken).ConfigureAwait(false);
                await _logger.WriteAsync("Tools", tool.Id, "Warning", "تم اكتشاف أداة تالفة أو غير موثوقة وسيتم إصلاحها.", null, cancellationToken);
            }

            var temp = target + ".part";
            if (File.Exists(temp)) File.Delete(temp);

            try
            {
                using var response = await _httpClient.GetAsync(tool.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                var validation = await ValidateAsync(temp, tool, cancellationToken).ConfigureAwait(false);
                if (!validation.Accepted)
                {
                    File.Delete(temp);
                    await WriteAuditAsync(tool, temp, validation, "DownloadedRejected", downloaded: true, cancellationToken).ConfigureAwait(false);
                    await _logger.WriteAsync(
                        "Tools",
                        tool.Id,
                        "Error",
                        "فشل التحقق الأمني من الأداة وتم حذف الملف.",
                        new
                        {
                            tool.DownloadUri,
                            ExpectedSha256 = tool.Sha256,
                            validation.ActualSha256,
                            validation.Signature.HasSignature,
                            validation.Signature.Trusted,
                            validation.Signature.SignerSubject,
                            validation.Reason
                        },
                        cancellationToken);
                    return new ToolReadyResult(tool, false, null, "فشل التحقق الأمني من الأداة، لذلك لم يتم تشغيلها.", true, false);
                }

                File.Move(temp, target, true);
                await WriteAuditAsync(tool, target, validation, "InstalledPortable", downloaded: true, cancellationToken).ConfigureAwait(false);
                await _logger.WriteAsync(
                    "Tools",
                    tool.Id,
                    "Information",
                    validation.Signature.Trusted
                        ? "تم تجهيز الأداة من مصدرها الرسمي والتحقق من SHA-256 وتوقيع Windows."
                        : "تم تجهيز الأداة من مصدرها الرسمي والتحقق من SHA-256؛ التوقيع غير مطلوب لهذه الأداة.",
                    new { target, tool.Version, validation.Signature.SignerSubject },
                    cancellationToken);
                return new ToolReadyResult(tool, true, target, "تم تجهيز الأداة بنجاح.", true, true);
            }
            catch
            {
                if (File.Exists(temp)) File.Delete(temp);
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await _logger.WriteAsync(
                "Tools",
                tool.Id,
                "Error",
                "تعذر تجهيز الأداة.",
                new
                {
                    ToolId = tool.Id,
                    tool.Version,
                    tool.Vendor,
                    OfficialSource = tool.DownloadUri.ToString(),
                    error = ex.Message,
                    Timestamp = DateTimeOffset.UtcNow
                },
                CancellationToken.None);
            return new ToolReadyResult(tool, false, null, "تعذر تجهيز الأداة حاليًا. سيستمر D7 بالوظائف التي لا تعتمد عليها.", false, false);
        }
        finally
        {
            gate.Release();
        }
    }

    public static async Task<bool> VerifyHashAsync(string filePath, string expectedSha256, CancellationToken cancellationToken)
    {
        var actualHex = await ComputeSha256Async(filePath, cancellationToken).ConfigureAwait(false);
        return string.Equals(actualHex, NormalizeHash(expectedSha256), StringComparison.Ordinal);
    }

    public static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        using var sha = SHA256.Create();
        var actual = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(actual).ToLowerInvariant();
    }

    private static async Task<FileValidationResult> ValidateAsync(
        string filePath,
        ToolDescriptor tool,
        CancellationToken cancellationToken)
    {
        var actual = await ComputeSha256Async(filePath, cancellationToken).ConfigureAwait(false);
        var hashMatches = string.Equals(actual, NormalizeHash(tool.Sha256), StringComparison.Ordinal);
        if (!hashMatches)
        {
            return new FileValidationResult(
                false,
                actual,
                new AuthenticodeVerificationResult(false, false, false, null, "Signature check skipped because SHA-256 mismatched."),
                "SHA256_MISMATCH");
        }

        if (!tool.VerifyAuthenticode)
        {
            return new FileValidationResult(
                true,
                actual,
                new AuthenticodeVerificationResult(false, false, false, null, "Authenticode check disabled by tool policy."),
                null);
        }

        var signature = AuthenticodeVerifier.Verify(filePath);
        if (signature.HasSignature && !signature.Trusted)
            return new FileValidationResult(false, actual, signature, "AUTHENTICODE_INVALID");
        if (tool.RequireTrustedSignature && !signature.Trusted)
            return new FileValidationResult(false, actual, signature, "AUTHENTICODE_REQUIRED");

        if (!string.IsNullOrWhiteSpace(tool.ExpectedSignerContains))
        {
            var signerMatches = signature.Trusted &&
                !string.IsNullOrWhiteSpace(signature.SignerSubject) &&
                signature.SignerSubject.Contains(tool.ExpectedSignerContains, StringComparison.OrdinalIgnoreCase);
            if (!signerMatches)
                return new FileValidationResult(false, actual, signature, "SIGNER_MISMATCH");
        }

        return new FileValidationResult(true, actual, signature, null);
    }

    private Task WriteAuditAsync(
        ToolDescriptor tool,
        string filePath,
        FileValidationResult validation,
        string installResult,
        bool downloaded,
        CancellationToken cancellationToken) =>
        _logger.WriteAsync(
            "ToolAcquisitionAudit",
            tool.Id,
            validation.Accepted ? "Information" : "Warning",
            validation.Accepted ? "اكتمل تحقق الأداة." : "فشل تحقق الأداة.",
            new
            {
                ToolId = tool.Id,
                tool.Version,
                tool.Vendor,
                OfficialSource = tool.DownloadUri.ToString(),
                tool.AssetName,
                FilePath = filePath,
                ExpectedSha256 = NormalizeHash(tool.Sha256),
                validation.ActualSha256,
                SignatureChecked = validation.Signature.Checked,
                SignaturePresent = validation.Signature.HasSignature,
                SignatureTrusted = validation.Signature.Trusted,
                SignatureSigner = validation.Signature.SignerSubject,
                SignatureDetail = validation.Signature.Detail,
                tool.RequireTrustedSignature,
                tool.ExpectedSignerContains,
                InstallResult = installResult,
                Downloaded = downloaded,
                validation.Reason,
                Timestamp = DateTimeOffset.UtcNow
            },
            cancellationToken);

    private static string NormalizeHash(string value) => value.Trim().ToLowerInvariant();

    private sealed record FileValidationResult(
        bool Accepted,
        string ActualSha256,
        AuthenticodeVerificationResult Signature,
        string? Reason);
}
