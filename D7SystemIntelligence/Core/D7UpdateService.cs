using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace D7SystemIntelligence.Core;

public sealed record D7UpdateInfo(
    bool UpdateAvailable,
    Version CurrentVersion,
    Version LatestVersion,
    string Tag,
    string InstallerUrl,
    string? ChecksumUrl,
    string? Digest,
    string ReleaseNotes);

public sealed class D7UpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/Emperor0/em/releases/latest";
    private const string InstallerAssetName = "D7-System-Intelligence-Setup.exe";
    private const string ChecksumAssetName = "D7-System-Intelligence-Setup.exe.sha256";
    private static readonly HttpClient Http = CreateClient();

    public Version CurrentVersion => NormalizeVersion(
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    public string CurrentVersionText => $"v{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    public async Task<D7UpdateInfo> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Http.GetAsync(LatestReleaseApi, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var root = doc.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagNode) ? tagNode.GetString() ?? string.Empty : string.Empty;
        var latest = ParseTag(tag);
        if (latest == null)
            throw new InvalidOperationException("تعذر قراءة رقم إصدار D7 المنشور.");

        var installerUrl = string.Empty;
        string? checksumUrl = null;
        string? digest = null;

        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? string.Empty : string.Empty;

                if (name.Equals(InstallerAssetName, StringComparison.OrdinalIgnoreCase))
                {
                    installerUrl = url;
                    if (asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String)
                        digest = d.GetString();
                }
                else if (name.Equals(ChecksumAssetName, StringComparison.OrdinalIgnoreCase))
                {
                    checksumUrl = url;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(installerUrl))
            throw new InvalidOperationException("إصدار D7 موجود، لكن ملف التثبيت غير موجود في صفحة الإصدار.");

        var notes = root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String
            ? body.GetString() ?? string.Empty
            : string.Empty;

        var current = CurrentVersion;
        return new D7UpdateInfo(
            latest > current,
            current,
            latest,
            tag,
            installerUrl,
            checksumUrl,
            digest,
            notes);
    }

    public async Task<string> DownloadAndVerifyAsync(
        D7UpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var root = Path.Combine(Path.GetTempPath(), "D7SystemIntelligence", "Updates", update.LatestVersion.ToString(3));
        Directory.CreateDirectory(root);
        var installerPath = Path.Combine(root, InstallerAssetName);

        using (var response = await Http.GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, true);

            var buffer = new byte[1024 * 128];
            long received = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken);
                if (read <= 0) break;
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
                if (total is > 0)
                    progress?.Report(Math.Clamp(received * 100.0 / total.Value, 0, 100));
            }
        }

        progress?.Report(100);

        var expected = ParseDigest(update.Digest);
        if (string.IsNullOrWhiteSpace(expected) && !string.IsNullOrWhiteSpace(update.ChecksumUrl))
        {
            var checksumText = await Http.GetStringAsync(update.ChecksumUrl, cancellationToken);
            expected = checksumText.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(expected))
            throw new InvalidOperationException("لم تتوفر بصمة SHA-256 للإصدار. أوقف D7 التحديث للحماية.");

        var actual = await ComputeSha256Async(installerPath, cancellationToken);
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(installerPath); } catch { }
            throw new InvalidOperationException("فشل التحقق من SHA-256. لم يتم تشغيل ملف التحديث.");
        }

        return installerPath;
    }

    public static void LaunchInstaller(string installerPath)
    {
        if (!File.Exists(installerPath))
            throw new FileNotFoundException("ملف تحديث D7 غير موجود.", installerPath);

        // /SILENT deliberately keeps Inno's progress UI visible. /VERYSILENT made a valid update
        // look like nothing happened on the user's machine.
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /D7UPDATE=1",
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(installerPath) ?? Path.GetTempPath()
        });

        if (process == null)
            throw new InvalidOperationException("Windows لم يبدأ مثبت D7. لم يتم إغلاق البرنامج حتى يظهر الخطأ بوضوح.");
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("D7SystemIntelligence-Updater/1.1");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static Version? ParseTag(string tag)
    {
        var raw = tag.Trim();
        foreach (var prefix in new[] { "d7-v", "D7-v", "v", "V" })
        {
            if (raw.StartsWith(prefix, StringComparison.Ordinal))
            {
                raw = raw[prefix.Length..];
                break;
            }
        }

        return Version.TryParse(raw, out var version) ? NormalizeVersion(version) : null;
    }

    private static Version NormalizeVersion(Version version)
        => new(version.Major, Math.Max(0, version.Minor), Math.Max(0, version.Build));

    private static string? ParseDigest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest)) return null;
        const string prefix = "sha256:";
        return digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? digest[prefix.Length..].Trim()
            : digest.Trim();
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
