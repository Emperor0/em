using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace D7SystemIntelligence.Core;

public sealed record OpenRgbBackendInfo(bool Available, string? ExecutablePath, string Detail);
public sealed record OpenRgbDevice(int Index, string Name, string Type, IReadOnlyList<string> Modes, string Raw);

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
            ? new OpenRgbBackendInfo(false, null, "OpenRGB غير موجود. D7KT يستطيع تنزيل Windows 64 الرسمي والتحقق من SHA-256.")
            : new OpenRgbBackendInfo(true, exe, $"OpenRGB backend جاهز: {exe}");
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
            throw new InvalidOperationException("لم يجد D7KT ملف OpenRGB Windows 64 ZIP في الإصدار الرسمي الحالي.");
        if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("الإصدار الرسمي لا يحتوي SHA-256؛ D7KT رفض تنزيل backend بدون تحقق.");

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
        return new OpenRgbBackendInfo(true, exe, $"تم تجهيز OpenRGB الرسمي {tag} والتحقق من SHA-256.");
    }

    public async Task<IReadOnlyList<OpenRgbDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var backend = Detect();
        if (!backend.Available || backend.ExecutablePath == null) return [];
        var run = await RunAsync(backend.ExecutablePath, "--list-devices", cancellationToken);
        if (run.ExitCode != 0) throw new InvalidOperationException($"OpenRGB list failed ({run.ExitCode}).\n{run.Output}");
        return ParseDevices(run.Output);
    }

    public async Task<string> ListDevicesAsync(CancellationToken cancellationToken = default)
    {
        var devices = await GetDevicesAsync(cancellationToken);
        if (devices.Count == 0) return "OpenRGB لم يرجع أجهزة RGB مدعومة.";
        return string.Join(Environment.NewLine + Environment.NewLine, devices.Select(d =>
            $"[{d.Index}] {d.Name}\nType: {d.Type}\nModes: {(d.Modes.Count == 0 ? "—" : string.Join(", ", d.Modes))}"));
    }

    public Task<string> SetColorAsync(string rgbHex, CancellationToken cancellationToken = default)
        => ApplyAsync(null, "static", rgbHex, null, cancellationToken);

    public Task<string> SetDeviceColorAsync(int deviceIndex, string rgbHex, CancellationToken cancellationToken = default)
        => ApplyAsync(deviceIndex, "static", rgbHex, null, cancellationToken);

    public Task<string> SetDeviceModeAsync(int deviceIndex, string mode, string? rgbHex = null, int? brightness = null, CancellationToken cancellationToken = default)
        => ApplyAsync(deviceIndex, mode, rgbHex, brightness, cancellationToken);

    public Task<string> SetAllModeAsync(string mode, string? rgbHex = null, int? brightness = null, CancellationToken cancellationToken = default)
        => ApplyAsync(null, mode, rgbHex, brightness, cancellationToken);

    public Task<string> TurnOffAsync(CancellationToken cancellationToken = default)
        => ApplyAsync(null, "static", "000000", 0, cancellationToken);

    public Task<string> TurnOffDeviceAsync(int deviceIndex, CancellationToken cancellationToken = default)
        => ApplyAsync(deviceIndex, "static", "000000", 0, cancellationToken);

    public async Task<string> SaveProfileAsync(string profileName, CancellationToken cancellationToken = default)
    {
        var backend = Detect();
        if (!backend.Available || backend.ExecutablePath == null) return backend.Detail;
        var name = SafeProfileName(profileName);
        if (string.IsNullOrWhiteSpace(name)) return "اسم Profile غير صالح.";
        var run = await RunAsync(backend.ExecutablePath, $"--save-profile \"{name}\"", cancellationToken);
        return run.ExitCode == 0 ? $"تم حفظ OpenRGB Profile: {name}" : $"فشل حفظ Profile ({run.ExitCode}).\n{run.Output}";
    }

    public async Task<string> LoadProfileAsync(string profileName, CancellationToken cancellationToken = default)
    {
        var backend = Detect();
        if (!backend.Available || backend.ExecutablePath == null) return backend.Detail;
        var name = SafeProfileName(profileName);
        if (string.IsNullOrWhiteSpace(name)) return "اسم Profile غير صالح.";
        var run = await RunAsync(backend.ExecutablePath, $"--profile \"{name}\"", cancellationToken);
        return run.ExitCode == 0 ? $"تم تحميل OpenRGB Profile: {name}" : $"فشل تحميل Profile ({run.ExitCode}).\n{run.Output}";
    }

    public string LaunchAdvancedStudio()
    {
        var backend = Detect();
        if (!backend.Available || backend.ExecutablePath == null) return backend.Detail;
        Process.Start(new ProcessStartInfo
        {
            FileName = backend.ExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(backend.ExecutablePath)!,
            UseShellExecute = true
        });
        return "تم فتح OpenRGB Advanced Studio. استخدمه للـzones/per-LED/plugins والخرائط البصرية؛ D7KT يبقى مسؤولًا عن Automation والProfiles الذكية.";
    }

    private async Task<string> ApplyAsync(int? deviceIndex, string? mode, string? rgbHex, int? brightness, CancellationToken cancellationToken)
    {
        var backend = Detect();
        if (!backend.Available || backend.ExecutablePath == null) return backend.Detail;

        var args = new List<string>();
        if (deviceIndex.HasValue) args.Add($"--device {deviceIndex.Value}");
        if (!string.IsNullOrWhiteSpace(mode)) args.Add($"--mode \"{mode.Trim().Replace("\"", string.Empty)}\"");
        if (!string.IsNullOrWhiteSpace(rgbHex))
        {
            var color = NormalizeColor(rgbHex);
            if (color == null) return "اللون غير صالح. استخدم RRGGBB مثل FF0000.";
            args.Add($"--color {color}");
        }
        if (brightness.HasValue)
            args.Add($"--brightness {Math.Clamp(brightness.Value, 0, 100)}");

        var run = await RunAsync(backend.ExecutablePath, string.Join(' ', args), cancellationToken);
        var target = deviceIndex.HasValue ? $"الجهاز #{deviceIndex.Value}" : "كل الأجهزة";
        return run.ExitCode == 0
            ? $"تم التطبيق على {target}. Mode={mode ?? "unchanged"} Color={(rgbHex ?? "unchanged")} Brightness={(brightness?.ToString() ?? "unchanged")}."
            : $"OpenRGB رفض التطبيق على {target}. ExitCode={run.ExitCode}\n{run.Output}";
    }

    private static IReadOnlyList<OpenRgbDevice> ParseDevices(string output)
    {
        var list = new List<OpenRgbDevice>();
        var lines = output.Replace("\r", string.Empty).Split('\n');
        int? index = null;
        string name = string.Empty;
        string type = string.Empty;
        var modes = new List<string>();
        var raw = new List<string>();

        void Flush()
        {
            if (!index.HasValue) return;
            list.Add(new OpenRgbDevice(index.Value, string.IsNullOrWhiteSpace(name) ? $"RGB Device {index.Value}" : name.Trim(), type.Trim(), modes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), string.Join(Environment.NewLine, raw)));
            index = null; name = string.Empty; type = string.Empty; modes.Clear(); raw.Clear();
        }

        foreach (var line in lines)
        {
            var match = Regex.Match(line, @"^\s*(\d+)\s*:\s*(.+)$");
            if (match.Success)
            {
                Flush();
                index = int.Parse(match.Groups[1].Value);
                name = match.Groups[2].Value.Trim();
                raw.Add(line);
                continue;
            }
            if (!index.HasValue) continue;
            raw.Add(line);
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Type:", StringComparison.OrdinalIgnoreCase)) type = trimmed[5..].Trim();
            if (trimmed.StartsWith("Modes:", StringComparison.OrdinalIgnoreCase))
            {
                modes.AddRange(trimmed[6..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            }
            else if (trimmed.StartsWith("Mode", StringComparison.OrdinalIgnoreCase) && trimmed.Contains(':'))
            {
                var value = trimmed[(trimmed.IndexOf(':') + 1)..].Trim();
                if (!string.IsNullOrWhiteSpace(value)) modes.Add(value);
            }
        }
        Flush();
        return list;
    }

    private static string? NormalizeColor(string value)
    {
        var raw = (value ?? string.Empty).Trim().TrimStart('#').ToUpperInvariant();
        return raw.Length == 6 && raw.All(Uri.IsHexDigit) ? raw : null;
    }

    private static string SafeProfileName(string value)
        => string.Concat((value ?? string.Empty).Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or ' ')).Trim();

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
        client.DefaultRequestHeaders.UserAgent.ParseAdd("D7SystemIntelligence-RGB/2.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}

public sealed class TemperatureRgbController : IDisposable
{
    private readonly HardwareEngine _hardware;
    private readonly ManagedOpenRgbService _rgb;
    private CancellationTokenSource? _cts;
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
        _ = Task.Run(() => LoopAsync(_cts.Token));
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
                    < 45 => "00D9FF",
                    < 55 => "00FF88",
                    < 65 => "B7FF00",
                    < 75 => "FFD000",
                    < 82 => "FF7200",
                    _ => "FF1538"
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
