using System.Diagnostics;
using System.Text.RegularExpressions;

namespace D7SystemIntelligence.Core;

public sealed record RgbStatus(
    bool BackendAvailable,
    string Backend,
    string Detail,
    string? ExecutablePath);

public sealed class RgbIntelligence
{
    public RgbStatus Detect()
    {
        var path = FindOpenRgb();
        return path == null
            ? new RgbStatus(false, "None", "OpenRGB غير مثبت أو غير موجود في PATH. التحكم يبقى غير متاح بدل إرسال أوامر عمياء للهاردوير.", null)
            : new RgbStatus(true, "OpenRGB", "OpenRGB متاح ويمكن استخدامه كطبقة توافق للأجهزة المدعومة.", path);
    }

    public async Task<string> SetSolidColorAsync(string rgbHex, CancellationToken cancellationToken = default)
    {
        var status = Detect();
        if (!status.BackendAvailable || status.ExecutablePath == null)
            return status.Detail;

        var color = NormalizeHex(rgbHex);
        if (color == null)
            return "لون غير صالح. استخدم صيغة RRGGBB مثل FF0000.";

        var result = await RunAsync(status.ExecutablePath, $"--mode static --color {color}", cancellationToken);
        return result.ExitCode == 0
            ? $"تم إرسال اللون #{color} إلى OpenRGB للأجهزة المدعومة."
            : $"OpenRGB رفض الأمر أو لم يتمكن من تطبيقه. ExitCode={result.ExitCode}\n{result.Output}";
    }

    public async Task<string> TurnOffAsync(CancellationToken cancellationToken = default)
        => await SetSolidColorAsync("000000", cancellationToken);

    public async Task<string> ListDevicesAsync(CancellationToken cancellationToken = default)
    {
        var status = Detect();
        if (!status.BackendAvailable || status.ExecutablePath == null)
            return status.Detail;

        var result = await RunAsync(status.ExecutablePath, "--list-devices", cancellationToken);
        return string.IsNullOrWhiteSpace(result.Output)
            ? $"OpenRGB انتهى برمز {result.ExitCode} بدون قائمة أجهزة."
            : result.Output;
    }

    private static string? NormalizeHex(string value)
    {
        var raw = value.Trim().TrimStart('#').ToUpperInvariant();
        return Regex.IsMatch(raw, "^[0-9A-F]{6}$") ? raw : null;
    }

    private static string? FindOpenRgb()
    {
        var candidates = new List<string>();
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        candidates.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => Path.Combine(p.Trim(), "OpenRGB.exe")));

        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        candidates.Add(Path.Combine(pf, "OpenRGB", "OpenRGB.exe"));
        candidates.Add(Path.Combine(pfx86, "OpenRGB", "OpenRGB.exe"));

        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string file,
        string arguments,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = file,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, (await stdout) + Environment.NewLine + (await stderr));
    }
}
