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

public sealed record StorageTrendRecord(string Drive, string Severity, string Metric, string Detail);

public sealed record StorageSnapshot(IReadOnlyList<PhysicalDriveRecord> Drives, IReadOnlyList<VolumeRecord> Volumes, string Summary)
{
    public IReadOnlyList<StorageTrendRecord> Trends { get; init; } = [];
}

internal sealed class StorageHistoryState
{
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.Now;
    public List<PhysicalDriveRecord> Drives { get; set; } = [];
}

public sealed class StorageIntelligenceService
{
    private readonly string _historyPath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public StorageIntelligenceService()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D7SystemIntelligence", "Storage");
        Directory.CreateDirectory(root);
        _historyPath = Path.Combine(root, "last-reliability-scan.json");
    }

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

            var trends = CompareWithHistory(drives);
            await SaveHistoryAsync(drives, cancellationToken);

            var unhealthy = drives.Count(x => !x.HealthStatus.Equals("Healthy", StringComparison.OrdinalIgnoreCase));
            var lowSpace = volumes.Count(x => x.FreePercent < 10);
            var hot = drives.Count(x => x.TemperatureC is >= 65);
            var summary = $"أقراص فعلية {drives.Count} • Volumes {volumes.Count} • Health warnings {unhealthy} • أقل من 10% مساحة {lowSpace} • حرارة ≥65°C {hot} • Reliability deltas {trends.Count}";
            return new StorageSnapshot(drives, volumes, summary) { Trends = trends };
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
        return run.ExitCode == 0 ? "Analyze completed via Windows Optimize-Volume.\n" + run.Output.Trim() : $"Analyze فشل: {run.Error}";
    }

    public async Task<string> RetrimVolumeAsync(string driveLetter, CancellationToken cancellationToken = default)
    {
        var drive = NormalizeDrive(driveLetter);
        var run = await RunPowerShellAsync($"Optimize-Volume -DriveLetter {drive} -ReTrim -Verbose | Out-String", cancellationToken);
        return run.ExitCode == 0
            ? "ReTrim command completed عبر Windows Optimize-Volume. هذا يثبت تنفيذ الأداة، وليس تحسن FPS أو سرعة وهمية.\n" + run.Output.Trim()
            : $"ReTrim فشل أو غير مدعوم على هذا القرص: {run.Error}";
    }

    private List<StorageTrendRecord> CompareWithHistory(IReadOnlyList<PhysicalDriveRecord> current)
    {
        StorageHistoryState? prior = null;
        if (File.Exists(_historyPath))
        {
            try { prior = JsonSerializer.Deserialize<StorageHistoryState>(File.ReadAllText(_historyPath), JsonOptions); }
            catch { }
        }

        var trends = new List<StorageTrendRecord>();
        if (prior == null) return trends;
        foreach (var drive in current)
        {
            var old = prior.Drives.FirstOrDefault(x => SameDrive(x, drive));
            if (old == null) continue;
            var name = string.IsNullOrWhiteSpace(drive.SerialNumber) ? drive.FriendlyName : $"{drive.FriendlyName} [{MaskSerial(drive.SerialNumber)}]";

            if (old.ReadErrors.HasValue && drive.ReadErrors.HasValue && drive.ReadErrors > old.ReadErrors)
                trends.Add(new(name, "Warning", "ReadErrors", $"{old.ReadErrors} → {drive.ReadErrors} (+{drive.ReadErrors - old.ReadErrors})"));
            if (old.WriteErrors.HasValue && drive.WriteErrors.HasValue && drive.WriteErrors > old.WriteErrors)
                trends.Add(new(name, "Warning", "WriteErrors", $"{old.WriteErrors} → {drive.WriteErrors} (+{drive.WriteErrors - old.WriteErrors})"));
            if (old.Wear.HasValue && drive.Wear.HasValue && drive.Wear > old.Wear)
                trends.Add(new(name, "Info", "Wear", $"{old.Wear} → {drive.Wear}. قيمة Wear vendor-dependent؛ D7KT يعرض Delta ولا يفسرها كعمر متبقٍ بدون تعريف الشركة."));
            if (old.HealthStatus.Equals("Healthy", StringComparison.OrdinalIgnoreCase) && !drive.HealthStatus.Equals("Healthy", StringComparison.OrdinalIgnoreCase))
                trends.Add(new(name, "Critical", "HealthStatus", $"Healthy → {drive.HealthStatus}"));
        }
        return trends;
    }

    private async Task SaveHistoryAsync(IReadOnlyList<PhysicalDriveRecord> drives, CancellationToken token)
    {
        var state = new StorageHistoryState { CapturedAt = DateTimeOffset.Now, Drives = drives.ToList() };
        await File.WriteAllTextAsync(_historyPath, JsonSerializer.Serialize(state, JsonOptions), token);
    }

    private static bool SameDrive(PhysicalDriveRecord a, PhysicalDriveRecord b)
    {
        if (!string.IsNullOrWhiteSpace(a.SerialNumber) && !string.IsNullOrWhiteSpace(b.SerialNumber))
            return a.SerialNumber.Trim().Equals(b.SerialNumber.Trim(), StringComparison.OrdinalIgnoreCase);
        return a.FriendlyName.Equals(b.FriendlyName, StringComparison.OrdinalIgnoreCase) && Math.Abs(a.SizeGb - b.SizeGb) < 2;
    }

    private static string MaskSerial(string serial)
    {
        var s = serial.Trim();
        return s.Length <= 4 ? "****" : "***" + s[^4..];
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
