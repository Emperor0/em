using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace D7SystemIntelligence.Core;

public sealed record FfmpegBackendInfo(bool Available, string? FfmpegPath, string? FfprobePath, string Detail);

public sealed class ManagedFfmpegService
{
    private const string LatestApi = "https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/latest";
    private static readonly HttpClient Http = CreateClient();
    private readonly string _root;

    public ManagedFfmpegService()
    {
        _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D7SystemIntelligence", "Tools", "FFmpeg");
        Directory.CreateDirectory(_root);
    }

    public FfmpegBackendInfo Detect()
    {
        var candidates = new List<string>();
        if (Directory.Exists(_root)) candidates.AddRange(Directory.EnumerateFiles(_root, "ffmpeg.exe", SearchOption.AllDirectories));
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        candidates.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Select(x => Path.Combine(x.Trim(), "ffmpeg.exe")));
        var ffmpeg = candidates.FirstOrDefault(File.Exists);
        if (ffmpeg == null) return new(false, null, null, "FFmpeg غير مجهز. D7 يستطيع تنزيل Windows x64 build من BtbN والتحقق من SHA-256 قبل استخدامه.");
        var ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg) ?? string.Empty, "ffprobe.exe");
        return new(true, ffmpeg, File.Exists(ffprobe) ? ffprobe : null, "FFmpeg جاهز داخل D7.");
    }

    public async Task<FfmpegBackendInfo> EnsureAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var current = Detect();
        if (current.Available) return current;

        using var response = await Http.GetAsync(LatestApi, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;
        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "latest" : "latest";

        string? url = null;
        string? digest = null;
        string? name = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var n = asset.TryGetProperty("name", out var p) ? p.GetString() ?? string.Empty : string.Empty;
                if (!n.EndsWith("win64-gpl-shared.zip", StringComparison.OrdinalIgnoreCase) &&
                    !n.EndsWith("win64-gpl.zip", StringComparison.OrdinalIgnoreCase)) continue;
                name = n;
                url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                digest = asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                if (!string.IsNullOrWhiteSpace(digest)) break;
            }
        }

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("لم يجد D7 FFmpeg Windows x64 ZIP في الإصدار الحالي.");
        if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("حزمة FFmpeg لا تعرض SHA-256 عبر GitHub Release؛ D7 رفض تشغيل ملف غير متحقق.");

        var versionRoot = Path.Combine(_root, Sanitize(tag));
        Directory.CreateDirectory(versionRoot);
        var zip = Path.Combine(versionRoot, name);
        using (var download = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            download.EnsureSuccessStatusCode();
            var total = download.Content.Headers.ContentLength;
            await using var input = await download.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(zip, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, true);
            var buffer = new byte[128 * 1024];
            long received = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken);
                if (read <= 0) break;
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
                if (total is > 0) progress?.Report(received * 100d / total.Value);
            }
        }

        var actual = await Sha256Async(zip, cancellationToken);
        var expected = digest[7..].Trim();
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(zip); } catch { }
            throw new InvalidOperationException("SHA-256 لحزمة FFmpeg غير مطابق؛ تم حذفها.");
        }

        var extract = Path.Combine(versionRoot, "app");
        if (Directory.Exists(extract)) Directory.Delete(extract, true);
        ZipFile.ExtractToDirectory(zip, extract, true);
        var ffmpeg = Directory.EnumerateFiles(extract, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
        var ffprobe = Directory.EnumerateFiles(extract, "ffprobe.exe", SearchOption.AllDirectories).FirstOrDefault();
        if (ffmpeg == null) throw new InvalidOperationException("تم فك الحزمة لكن ffmpeg.exe غير موجود.");
        progress?.Report(100);
        return new(true, ffmpeg, ffprobe, $"تم تجهيز FFmpeg {tag} والتحقق من SHA-256.");
    }

    public async Task<(int ExitCode, string Output, string Error)> RunAsync(string arguments, CancellationToken cancellationToken = default)
    {
        var backend = await EnsureAsync(null, cancellationToken);
        if (backend.FfmpegPath == null) throw new InvalidOperationException(backend.Detail);
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = backend.FfmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(backend.FfmpegPath) ?? Environment.CurrentDirectory
            }
        };
        p.Start();
        var output = p.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = p.StandardError.ReadToEndAsync(cancellationToken);
        await p.WaitForExitAsync(cancellationToken);
        return (p.ExitCode, (await output).Trim(), (await error).Trim());
    }

    public async Task<double?> GetDurationSecondsAsync(string file, CancellationToken cancellationToken = default)
    {
        var backend = Detect();
        if (!backend.Available || backend.FfprobePath == null) return null;
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = backend.FfprobePath,
                Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{file}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        p.Start();
        var text = await p.StandardOutput.ReadToEndAsync(cancellationToken);
        await p.WaitForExitAsync(cancellationToken);
        return double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) ? seconds : null;
    }

    private static async Task<string> Sha256Async(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
        using var sha = SHA256.Create();
        return Convert.ToHexString(await sha.ComputeHashAsync(stream, token)).ToLowerInvariant();
    }

    private static string Sanitize(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("D7SystemIntelligence-FFmpeg/1.0");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }
}
