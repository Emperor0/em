using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace D7SystemIntelligence.Core;

public sealed record OpenRgbBackendInfo(bool Available, string? ExecutablePath, string Detail);

public sealed class ManagedOpenRgbService
{
    private const string LatestApi = "https://api.github.com/repos/CalcProgrammer1/OpenRGB/releases/latest";
    private static readonly HttpClient Http = CreateClient();
    private readonly string _root;

    public ManagedOpenRgbService()
    {
        _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D7SystemIntelligence", "Tools", "OpenRGB");
        Directory.CreateDirectory(_root);
    }

    public OpenRgbBackendInfo Detect()
    {
        var candidates = new List<string>();
        if (Directory.Exists(_root))
            candidates.AddRange(Directory.EnumerateFiles(_root, "OpenRGB.exe", SearchOption.AllDirectories));

        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        candidates.Add(Path.Combine(pf, "OpenRGB", "OpenRGB.exe"));
        candidates.Add(Path.Combine(pfx86, "OpenRGB", "OpenRGB.exe"));
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        candidates.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Select(p => Path.Combine(p.Trim(), "OpenRGB.exe")));

        var exe = candidates.FirstOrDefault(File.Exists);
        return exe == null
            ? new OpenRgbBackendInfo(false, null, "OpenRGB غير موجود. D7 يستطيع تنزيل نسخة Windows 64 الرسمية والتحقق من SHA-256 عند الضغط على تجهيز OpenRGB.")
            : new OpenRgbBackendInfo(true, exe, $"OpenRGB جاهز: {exe}");
    }

    public async Task<OpenRgbBackendInfo> EnsureAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
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
        string? assetName = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? string.Empty;
                if (!name.Contains("Windows_64", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                assetName = name;
                url = asset.GetProperty("browser_download_url").GetString();
                if (asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String) digest = d.GetString();
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(assetName))
            throw new InvalidOperationException("لم يجد D7 ملف OpenRGB Windows 64 ZIP في الإصدار الرسمي الحالي.");
        if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("الإصدار الرسمي لا يحتوي SHA-256؛ D7 رفض تنزيل أداة RGB بدون تحقق.");

        var versionRoot = Path.Combine(_root, Sanitize(tag));
        Directory.CreateDirectory(versionRoot);
        var zipPath = Path.Combine(versionRoot, assetName);

        using (var download = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            download.EnsureSuccessStatusCode();
            var total = download.Content.Headers.ContentLength;
            await using var input = await download.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, true);
            var buffer = new byte[128 * 1024];
            long received = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken);
                if (read <= 0) break;
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
                if (total is > 0) progress?.Report(received * 100.0 / total.Value);
            }
        }

        var expected = digest["sha256:".Length..].Trim();
        var actual = await Sha256Async(zipPath, cancellationToken);
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(zipPath); } catch { }
            throw new InvalidOperationException("فشل SHA-256 لملف OpenRGB. تم حذف الملف ولم يتم تشغيله.");
        }

        var extractRoot = Path.Combine(versionRoot, "app");
        if (Directory.Exists(extractRoot)) Directory.Delete(extractRoot, true);
        ZipFile.ExtractToDirectory(zipPath, extractRoot, true);
        var exe = Directory.EnumerateFiles(extractRoot, "OpenRGB.exe", SearchOption.AllDirectories).FirstOrDefault();
        if (exe == null) throw new InvalidOperationException("تم فك OpenRGB لكن OpenRGB.exe غير موجود داخل الحزمة.");

        progress?.Report(100);
        return new OpenRgbBackendInfo(true, exe, $"تم تنزيل OpenRGB الرسمي {tag} والتحقق من SHA-256 وتجهيزه داخل D7.");
    }

    public async Task<string> ListDevicesAsync(CancellationToken cancellationToken = default)
    {
        var backend = Detect();
        if (!backend.Available || backend.ExecutablePath == null) return backend.Detail;
        var run = await RunAsync(backend.ExecutablePath, "--list-devices", cancellationToken);
        return run.ExitCode == 0 && !string.IsNullOrWhiteSpace(run.Output) ? run.Output.Trim() : $"OpenRGB لم يرجع أجهزة. ExitCode={run.ExitCode}\n{run.Output}";
    }

    public async Task<string> SetColorAsync(string rgbHex, CancellationToken cancellationToken = default)
    {
        var backend = Detect();
        if (!backend.Available || backend.ExecutablePath == null) return backend.Detail;
        var color = NormalizeColor(rgbHex);
        if (color == null) return "اللون غير صالح. استخدم RRGGBB مثل FF0000.";
        var run = await RunAsync(backend.ExecutablePath, $"--mode static --color {color}", cancellationToken);
        return run.ExitCode == 0 ? $"تم تطبيق #{color} على أجهزة OpenRGB المدعومة." : $"OpenRGB رفض التطبيق. ExitCode={run.ExitCode}\n{run.Output}";
    }

    public Task<string> TurnOffAsync(CancellationToken cancellationToken = default) => SetColorAsync("000000", cancellationToken);

    private static string? NormalizeColor(string value)
    {
        var raw = (value ?? string.Empty).Trim().TrimStart('#').ToUpperInvariant();
        return raw.Length == 6 && raw.All(Uri.IsHexDigit) ? raw : null;
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string file, string arguments, CancellationToken cancellationToken)
    {
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = file,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(file) ?? Environment.CurrentDirectory
            }
        };
        p.Start();
        var stdout = p.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = p.StandardError.ReadToEndAsync(cancellationToken);
        await p.WaitForExitAsync(cancellationToken);
        return (p.ExitCode, (await stdout) + Environment.NewLine + (await stderr));
    }

    private static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
        using var sha = SHA256.Create();
        return Convert.ToHexString(await sha.ComputeHashAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static string Sanitize(string value)
        => string.Concat(value.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("D7SystemIntelligence-RGB/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}

public sealed class TemperatureRgbController : IDisposable
{
    private readonly HardwareEngine _hardware;
    private readonly ManagedOpenRgbService _rgb;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private string? _lastColor;
    public event Action<string>? StatusChanged;
    public bool IsRunning => _cts is { IsCancellationRequested: false };

    public TemperatureRgbController(HardwareEngine hardware, ManagedOpenRgbService rgb)
    {
        _hardware = hardware;
        _rgb = rgb;
    }

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
        StatusChanged?.Invoke("Temperature RGB بدأ. اللون يتغير حسب أعلى حرارة CPU/GPU.");
    }

    public void Stop()
    {
        if (_cts == null) return;
        _cts.Cancel();
        _cts = null;
        _lastColor = null;
        StatusChanged?.Invoke("Temperature RGB توقف.");
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _rgb.EnsureAsync(null, cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                var s = _hardware.Read();
                var temp = Math.Max(s.CpuTemp, s.GpuTemp);
                var color = temp switch
                {
                    < 50 => "00FF66",
                    < 60 => "7DFF00",
                    < 70 => "FFD000",
                    < 80 => "FF7A00",
                    _ => "FF0000"
                };
                if (!string.Equals(color, _lastColor, StringComparison.OrdinalIgnoreCase))
                {
                    var result = await _rgb.SetColorAsync(color, cancellationToken);
                    _lastColor = color;
                    StatusChanged?.Invoke($"Temperature RGB • {temp:0}°C • #{color} • {result}");
                }
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { StatusChanged?.Invoke("Temperature RGB: " + ex.Message); }
    }

    public void Dispose() => Stop();
}
