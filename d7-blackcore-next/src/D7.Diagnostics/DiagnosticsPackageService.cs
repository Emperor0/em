using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using D7.Core.Foundation;
using D7.Core.Logging;
using D7.Hardware.Models;

namespace D7.Diagnostics;

public sealed record DiagnosticsPackageResult(
    bool Success,
    string? ZipPath,
    string MessageAr,
    string? Error = null);

public sealed class DiagnosticsPackageService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AppPaths _paths;
    private readonly JsonLineLogger _logger;

    public DiagnosticsPackageService(AppPaths paths, JsonLineLogger logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task<DiagnosticsPackageResult> CreateAsync(
        HardwareSnapshot hardware,
        bool safeMode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hardware);
        _paths.EnsureCreated();

        var id = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
        var working = Path.Combine(_paths.Diagnostics, $"work_{id}_{Guid.NewGuid():N}");
        var zipPath = Path.Combine(_paths.Diagnostics, $"D7_Diagnostics_{id}.zip");

        try
        {
            Directory.CreateDirectory(working);
            cancellationToken.ThrowIfCancellationRequested();

            var summary = new
            {
                generatedAt = DateTimeOffset.UtcNow,
                app = new
                {
                    name = "D7 BLACKCORE",
                    version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "dev",
                    framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                    architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                    safeMode
                },
                windows = new
                {
                    hardware.Windows.ProductName,
                    hardware.Windows.DisplayVersion,
                    hardware.Windows.Build,
                    hardware.Windows.Ubr,
                    hardware.Windows.GameMode,
                    hardware.Windows.Hags,
                    hardware.Windows.GameDvr,
                    hardware.Windows.VbsConfigured,
                    hardware.Windows.MemoryIntegrityConfigured,
                    hardware.Windows.SecureBoot,
                    pagefileCount = hardware.Windows.PagefileEntries.Count
                },
                hardware = new
                {
                    cpu = hardware.Cpu,
                    memory = new
                    {
                        hardware.Memory.TotalGiB,
                        hardware.Memory.AvailableGiB,
                        hardware.Memory.UsedPercent
                    },
                    board = hardware.Board,
                    displays = hardware.DisplayAdapters.Select(x => new
                    {
                        x.Name,
                        x.DeviceName,
                        x.DeviceId,
                        x.AttachedToDesktop,
                        x.Primary,
                        x.MirroringDriver
                    }).ToArray(),
                    storage = hardware.Storage.Select(x => new
                    {
                        x.Name,
                        x.RootPath,
                        x.DriveType,
                        x.FileSystem,
                        x.TotalGiB,
                        x.FreeGiB
                    }).ToArray(),
                    network = hardware.NetworkAdapters.Select(x => new
                    {
                        x.Name,
                        x.Description,
                        x.InterfaceType,
                        x.Status,
                        x.SpeedBitsPerSecond,
                        x.SupportsIpv4,
                        x.SupportsIpv6
                    }).ToArray(),
                    startupEntryCount = hardware.StartupEntries.Count
                }
            };

            await File.WriteAllTextAsync(
                Path.Combine(working, "summary.json"),
                JsonSerializer.Serialize(summary, JsonOptions),
                cancellationToken).ConfigureAwait(false);

            await File.WriteAllTextAsync(
                Path.Combine(working, "privacy.txt"),
                "يتضمن التقرير فقط ملفات D7 المعروفة: ملخص النظام، سجلات D7، سجل العمليات وملفات measurement.json. لا يجمع كلمات مرور أو Cookies أو سجل المتصفح أو الرسائل أو Tokens أو الملفات الشخصية.",
                cancellationToken).ConfigureAwait(false);

            CopyKnownFiles(_paths.Logs, Path.Combine(working, "logs"), "*.jsonl", recursive: false, maxFiles: 12);
            CopyKnownFiles(_paths.Transactions, Path.Combine(working, "transactions"), "*.json", recursive: false, maxFiles: 40);
            CopyKnownFiles(_paths.Measurements, Path.Combine(working, "measurements"), "measurement.json", recursive: true, maxFiles: 30);

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(working, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

            await _logger.WriteAsync(
                "Diagnostics",
                "PACKAGE",
                "Information",
                "تم إنشاء تقرير تشخيص آمن.",
                new { zipPath },
                CancellationToken.None).ConfigureAwait(false);

            return new DiagnosticsPackageResult(true, zipPath, "تم إنشاء تقرير التشخيص بنجاح.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await _logger.WriteAsync(
                "Diagnostics",
                "PACKAGE",
                "Error",
                "تعذر إنشاء تقرير التشخيص.",
                new { ex.Message },
                CancellationToken.None).ConfigureAwait(false);
            return new DiagnosticsPackageResult(false, null, "تعذر إنشاء تقرير التشخيص حاليًا.", ex.Message);
        }
        finally
        {
            try
            {
                if (Directory.Exists(working)) Directory.Delete(working, recursive: true);
            }
            catch
            {
                // Cleanup failure must not hide the package result.
            }
        }
    }

    private static void CopyKnownFiles(
        string source,
        string destination,
        string searchPattern,
        bool recursive,
        int maxFiles)
    {
        if (!Directory.Exists(source)) return;

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.EnumerateFiles(source, searchPattern, option)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(maxFiles)
            .ToArray();

        if (files.Length == 0) return;
        Directory.CreateDirectory(destination);

        foreach (var file in files)
        {
            var relativeName = recursive
                ? SanitizeRelativePath(Path.GetRelativePath(source, file.FullName))
                : file.Name;
            var target = Path.Combine(destination, relativeName);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            file.CopyTo(target, overwrite: true);
        }
    }

    private static string SanitizeRelativePath(string relativePath)
    {
        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(x => !string.IsNullOrWhiteSpace(x) && x != "." && x != "..")
            .Select(x => string.Concat(x.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)));
        return Path.Combine(parts.ToArray());
    }
}
