using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace D7SystemIntelligence.Core;

public sealed record PhysicalDriveRecord(
    string FriendlyName,
    string MediaType,
    string HealthStatus,
    double SizeGb,
    double? TemperatureC,
    long? PowerOnHours,
    long? ReadErrors,
    long? WriteErrors,
    long? Wear,
    string SerialNumber);

public sealed record VolumeRecord(
    string DriveLetter,
    string FileSystem,
    string HealthStatus,
    double SizeGb,
    double FreeGb,
    double FreePercent,
    string Path);

public sealed record StorageSnapshot(IReadOnlyList<PhysicalDriveRecord> Drives, IReadOnlyList<VolumeRecord> Volumes, string Summary);

public sealed class StorageIntelligenceService
{
    public async Task<StorageSnapshot> ScanAsync(CancellationToken cancellationToken = default)
    {
        const string script = @"
$drives = @()
Get-PhysicalDisk -ErrorAction SilentlyContinue | ForEach-Object {
  $d = $_
  $r = $null
  try { $r = $d | Get-StorageReliabilityCounter -ErrorAction Stop } catch {}
  $drives += [pscustomobject]@{
    FriendlyName=$d.FriendlyName
    MediaType=$d.MediaType.ToString()
    HealthStatus=$d.HealthStatus.ToString()
    Size=[double]$d.Size
    Temperature=if($r){$r.Temperature}else{$null}
    PowerOnHours=if($r){$r.PowerOnHours}else{$null}
    ReadErrors=if($r){$r.ReadErrorsTotal}else{$null}
    WriteErrors=if($r){$r.WriteErrorsTotal}else{$null}
    Wear=if($r){$r.Wear}else{$null}
    SerialNumber=$d.SerialNumber
  }
}
$volumes = @()
Get-Volume -ErrorAction SilentlyContinue | Where-Object {$_.DriveType -eq 'Fixed' -and $_.DriveLetter} | ForEach-Object {
  $volumes += [pscustomobject]@{
    DriveLetter=$_.DriveLetter.ToString()
    FileSystem=$_.FileSystem
    HealthStatus=$_.HealthStatus.ToString()
    Size=[double]$_.Size
    SizeRemaining=[double]$_.SizeRemaining
    Path=($_.Path | Select-Object -First 1)
  }
}
[pscustomobject]@{Drives=$drives;Volumes=$volumes} | ConvertTo-Json -Depth 6 -Compress
";
        var run = await RunPowerShellAsync(script, cancellationToken);
        if (run.ExitCode != 0 || string.IsNullOrWhiteSpace(run.Output))
            return new StorageSnapshot([], [], "تعذر قراءة Windows Storage API: " + run.Error);

        try
        {
            using var doc = JsonDocument.Parse(run.Output);
            var drives = new List<PhysicalDriveRecord>();
            var volumes = new List<VolumeRecord>();
            if (doc.RootElement.TryGetProperty("Drives", out var dArr) && dArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in dArr.EnumerateArray())
                {
                    var size = Num(d, "Size") / 1_073_741_824d;
                    drives.Add(new PhysicalDriveRecord(
                        S(d,"FriendlyName"), S(d,"MediaType"), S(d,"HealthStatus"), size,
                        NullableDouble(d,"Temperature"), NullableLong(d,"PowerOnHours"), NullableLong(d,"ReadErrors"), NullableLong(d,"WriteErrors"), NullableLong(d,"Wear"), S(d,"SerialNumber")));
                }
            }
            if (doc.RootElement.TryGetProperty("Volumes", out var vArr) && vArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var v in vArr.EnumerateArray())
                {
                    var sizeBytes = Num(v,"Size");
                    var freeBytes = Num(v,"SizeRemaining");
                    var pct = sizeBytes > 0 ? freeBytes * 100d / sizeBytes : 0;
                    volumes.Add(new VolumeRecord(
                        S(v,"DriveLetter") + ":", S(v,"FileSystem"), S(v,"HealthStatus"),
                        sizeBytes / 1_073_741_824d, freeBytes / 1_073_741_824d, pct, S(v,"Path")));
                }
            }

            var unhealthy = drives.Count(x => !x.HealthStatus.Equals("Healthy", StringComparison.OrdinalIgnoreCase));
            var lowSpace = volumes.Count(x => x.FreePercent < 10);
            var summary = $"أقراص فعلية {drives.Count} • Volumes {volumes.Count} • Health warnings {unhealthy} • مساحات أقل من 10%: {lowSpace}";
            return new StorageSnapshot(drives, volumes, summary);
        }
        catch (Exception ex)
        {
            return new StorageSnapshot([], [], "تعذر تحليل بيانات التخزين: " + ex.Message);
        }
    }

    public async Task<string> AnalyzeVolumeAsync(string driveLetter, CancellationToken cancellationToken = default)
    {
        var drive = NormalizeDrive(driveLetter);
        var run = await RunPowerShellAsync($"Optimize-Volume -DriveLetter {drive} -Analyze -Verbose | Out-String", cancellationToken);
        return run.ExitCode == 0 ? run.Output.Trim() : $"Analyze فشل: {run.Error}";
    }

    public async Task<string> RetrimVolumeAsync(string driveLetter, CancellationToken cancellationToken = default)
    {
        var drive = NormalizeDrive(driveLetter);
        var run = await RunPowerShellAsync($"Optimize-Volume -DriveLetter {drive} -ReTrim -Verbose | Out-String", cancellationToken);
        return run.ExitCode == 0 ? "تم إرسال ReTrim عبر Windows Optimize-Volume.\n" + run.Output.Trim() : $"ReTrim فشل أو غير مدعوم على هذا القرص: {run.Error}";
    }

    private static string NormalizeDrive(string value)
    {
        var raw = (value ?? string.Empty).Trim().TrimEnd(':','\\');
        if (raw.Length != 1 || !char.IsLetter(raw[0])) throw new ArgumentException("Drive letter غير صالح.");
        return char.ToUpperInvariant(raw[0]).ToString();
    }

    private static string S(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind != JsonValueKind.Null ? p.ToString() : string.Empty;
    private static double Num(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var p) || p.ValueKind == JsonValueKind.Null) return 0;
        return p.TryGetDouble(out var v) ? v : double.TryParse(p.ToString(), out v) ? v : 0;
    }
    private static double? NullableDouble(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var p) || p.ValueKind == JsonValueKind.Null || p.ToString() == string.Empty) return null;
        return p.TryGetDouble(out var v) ? v : double.TryParse(p.ToString(), out v) ? v : null;
    }
    private static long? NullableLong(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var p) || p.ValueKind == JsonValueKind.Null || p.ToString() == string.Empty) return null;
        return p.TryGetInt64(out var v) ? v : long.TryParse(p.ToString(), out v) ? v : null;
    }

    private static async Task<(int ExitCode,string Output,string Error)> RunPowerShellAsync(string script, CancellationToken cancellationToken)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName="powershell.exe", Arguments=$"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                UseShellExecute=false, RedirectStandardOutput=true, RedirectStandardError=true, CreateNoWindow=true
            }
        };
        p.Start();
        var stdout=p.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr=p.StandardError.ReadToEndAsync(cancellationToken);
        await p.WaitForExitAsync(cancellationToken);
        return (p.ExitCode,(await stdout).Trim(),(await stderr).Trim());
    }
}
