using System.Diagnostics;

namespace D7SystemIntelligence.Core;

public static class SystemActions
{
    public static Task<string> RunWingetUpgradeScanAsync() => Run("winget", "upgrade --accept-source-agreements");

    public static Task<string> UpgradeAppsAsync() => Run(
        "winget",
        "upgrade --all --silent --accept-package-agreements --accept-source-agreements");

    public static async Task<string> RunWindowsRepairScanAsync()
    {
        var dism = await Run("dism.exe", "/Online /Cleanup-Image /ScanHealth");
        var sfc = await Run("sfc.exe", "/verifyonly");
        return dism + Environment.NewLine + sfc;
    }

    public static async Task<string> RepairWindowsAsync()
    {
        var dism = await Run("dism.exe", "/Online /Cleanup-Image /RestoreHealth");
        var sfc = await Run("sfc.exe", "/scannow");
        return "D7 Windows Repair\r\n" + dism + Environment.NewLine + sfc;
    }

    public static Task<string> FlushDnsAsync() => Run("ipconfig.exe", "/flushdns");

    public static Task<string> CheckSystemDriveAsync()
    {
        var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
        var drive = root.TrimEnd('\\');
        return Run("chkdsk.exe", $"{drive} /scan");
    }

    public static async Task<string> CleanSafeTemporaryFilesAsync(CancellationToken cancellationToken = default)
    {
        var roots = new[]
        {
            Path.GetTempPath(),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")
        }
        .Where(Directory.Exists)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        long freed = 0;
        var deleted = 0;
        var skipped = 0;
        var cutoff = DateTime.UtcNow.AddHours(-24);

        foreach (var root in roots)
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories); }
            catch { continue; }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTimeUtc > cutoff)
                    {
                        skipped++;
                        continue;
                    }

                    var size = info.Length;
                    info.Delete();
                    freed += size;
                    deleted++;
                }
                catch { skipped++; }
            }
        }

        return $"تم تنظيف الملفات المؤقتة القديمة الآمنة فقط. حذف {deleted} ملف • وفر {FormatBytes(freed)} • تخطى {skipped} ملف مستخدم/حديث.";
    }

    private static string FormatBytes(long value)
    {
        if (value >= 1024L * 1024 * 1024) return $"{value / 1024d / 1024 / 1024:0.00} GB";
        if (value >= 1024L * 1024) return $"{value / 1024d / 1024:0.0} MB";
        if (value >= 1024L) return $"{value / 1024d:0.0} KB";
        return $"{value} B";
    }

    private static async Task<string> Run(string file, string args)
    {
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo(file, args)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            p.Start();
            var output = p.StandardOutput.ReadToEndAsync();
            var err = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            return $"> {file} {args}\r\nExitCode: {p.ExitCode}\r\n{await output}\r\n{await err}";
        }
        catch (Exception ex)
        {
            return $"> {file} {args}\r\nفشل التشغيل: {ex.Message}";
        }
    }
}
