using System.Diagnostics;

namespace D7SystemIntelligence.Core;

public static class SystemActions
{
    public static Task<string> RunWingetUpgradeScanAsync() => Run("winget", "upgrade --accept-source-agreements");
    public static Task<string> UpgradeAppsAsync() => Run("winget", "upgrade --all --silent --accept-package-agreements --accept-source-agreements");
    public static async Task<string> RunWindowsRepairScanAsync()
    {
        var dism = await Run("dism.exe", "/Online /Cleanup-Image /ScanHealth");
        var sfc = await Run("sfc.exe", "/verifyonly");
        return dism + Environment.NewLine + sfc;
    }
    private static async Task<string> Run(string file, string args)
    {
        try
        {
            var p = new Process { StartInfo = new ProcessStartInfo(file, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } };
            p.Start(); var output = await p.StandardOutput.ReadToEndAsync(); var err = await p.StandardError.ReadToEndAsync(); await p.WaitForExitAsync(); return $"> {file} {args}\r\n{output}\r\n{err}";
        } catch (Exception ex) { return ex.Message; }
    }
}
