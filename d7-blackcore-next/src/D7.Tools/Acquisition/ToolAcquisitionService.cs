using System.Collections.Concurrent;
using System.Security.Cryptography;
using D7.Core.Foundation;
using D7.Core.Logging;
using D7.Tools.Models;

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
                if (await VerifyHashAsync(target, tool.Sha256, cancellationToken).ConfigureAwait(false))
                {
                    return new ToolReadyResult(tool, true, target, "الأداة جاهزة وتم التحقق من سلامتها.", false, false);
                }

                File.Delete(target);
                await _logger.WriteAsync("Tools", tool.Id, "Warning", "تم اكتشاف أداة تالفة وسيتم إصلاحها.", null, cancellationToken);
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
                }

                if (!await VerifyHashAsync(temp, tool.Sha256, cancellationToken).ConfigureAwait(false))
                {
                    File.Delete(temp);
                    await _logger.WriteAsync("Tools", tool.Id, "Error", "فشل التحقق من بصمة الأداة وتم حذف الملف.", new { tool.DownloadUri, tool.Sha256 }, cancellationToken);
                    return new ToolReadyResult(tool, false, null, "فشل التحقق الأمني من الأداة، لذلك لم يتم تشغيلها.", true, false);
                }

                File.Move(temp, target, true);
                await _logger.WriteAsync("Tools", tool.Id, "Information", "تم تجهيز الأداة من مصدرها الرسمي والتحقق من SHA-256.", new { target, tool.Version }, cancellationToken);
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
            await _logger.WriteAsync("Tools", tool.Id, "Error", "تعذر تجهيز الأداة.", new { error = ex.Message }, CancellationToken.None);
            return new ToolReadyResult(tool, false, null, "تعذر تجهيز الأداة حاليًا. سيستمر D7 بالوظائف التي لا تعتمد عليها.", false, false);
        }
        finally
        {
            gate.Release();
        }
    }

    public static async Task<bool> VerifyHashAsync(string filePath, string expectedSha256, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        using var sha = SHA256.Create();
        var actual = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        var actualHex = Convert.ToHexString(actual).ToLowerInvariant();
        return string.Equals(actualHex, expectedSha256.Trim().ToLowerInvariant(), StringComparison.Ordinal);
    }
}
