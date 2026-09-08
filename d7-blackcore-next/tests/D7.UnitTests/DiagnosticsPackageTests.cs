using System.IO.Compression;
using D7.Core.Foundation;
using D7.Core.Logging;
using D7.Diagnostics;
using D7.Hardware.Models;
using Xunit;

namespace D7.UnitTests;

public sealed class DiagnosticsPackageTests
{
    [Fact]
    public async Task Package_IncludesOnlyKnownD7DiagnosticsInputs()
    {
        var root = Path.Combine(Path.GetTempPath(), "D7-Diagnostics-Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(Path.Combine(root, "pd"), Path.Combine(root, "la"));
            paths.EnsureCreated();

            await File.WriteAllTextAsync(Path.Combine(paths.Logs, "app.jsonl"), "{\"message\":\"test\"}");
            await File.WriteAllTextAsync(Path.Combine(paths.Transactions, "tx.json"), "{\"transaction\":1}");
            var measurementDir = Path.Combine(paths.Measurements, "run1");
            Directory.CreateDirectory(measurementDir);
            await File.WriteAllTextAsync(Path.Combine(measurementDir, "measurement.json"), "{\"fps\":100}");
            await File.WriteAllTextAsync(Path.Combine(measurementDir, "presentmon.csv"), "secret,raw,csv");
            await File.WriteAllTextAsync(Path.Combine(paths.ProgramDataRoot, "cookies.txt"), "must-not-be-included");

            var service = new DiagnosticsPackageService(paths, new JsonLineLogger(paths));
            var result = await service.CreateAsync(Hardware(), safeMode: false, CancellationToken.None);

            Assert.True(result.Success);
            Assert.NotNull(result.ZipPath);
            Assert.True(File.Exists(result.ZipPath));

            using var archive = ZipFile.OpenRead(result.ZipPath!);
            var names = archive.Entries.Select(x => x.FullName.Replace('\\', '/')).ToArray();

            Assert.Contains("summary.json", names);
            Assert.Contains("privacy.txt", names);
            Assert.Contains("logs/app.jsonl", names);
            Assert.Contains("transactions/tx.json", names);
            Assert.Contains(names, x => x.StartsWith("measurements/", StringComparison.Ordinal) && x.EndsWith("measurement.json", StringComparison.Ordinal));
            Assert.DoesNotContain(names, x => x.EndsWith("presentmon.csv", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(names, x => x.Contains("cookies", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    private static HardwareSnapshot Hardware() => new(
        DateTimeOffset.UtcNow,
        new CpuInfo("CPU", 12, "X64"),
        new MemoryInfo(16UL * 1024 * 1024 * 1024, 8UL * 1024 * 1024 * 1024),
        new BoardInfo("BoardVendor", "Board", "BIOS", "Date"),
        new WindowsInfo(
            "Windows 10 Pro",
            "22H2",
            "19045",
            0,
            TriState.Enabled,
            TriState.Enabled,
            TriState.Disabled,
            TriState.Disabled,
            TriState.Disabled,
            TriState.Enabled,
            Array.Empty<string>()),
        [new DisplayAdapterInfo("GPU", "DISPLAY1", "PCI\\VEN_TEST", true, true, false)],
        [new StorageInfo("Disk", "C:\\", "Fixed", "NTFS", 1000, 500)],
        [new NetworkAdapterInfo("LAN", "Ethernet", "Ethernet", "Up", 1_000_000_000, true, true)],
        [new StartupEntry("HKCU", "Example", "example.exe")]);
}
