namespace D7SystemIntelligence.Core;

public sealed record RestoreVaultRecord(
    string Type,
    string Name,
    string Path,
    DateTime LastWriteTime,
    long SizeBytes,
    string Action);

public sealed record RestoreVaultActionResult(bool Success, string Detail);

public sealed class RestoreVaultService
{
    private readonly string _root;
    private readonly AudioControlService _audio = new();
    private readonly NetworkGamingProfileService _network = new();
    private readonly PowerPlanService _power = new();
    private readonly DriverSafetyService _drivers = new();

    public RestoreVaultService()
    {
        _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D7SystemIntelligence", "RestoreVault");
        Directory.CreateDirectory(_root);
    }

    public string RootPath => _root;

    public IReadOnlyList<RestoreVaultRecord> Scan()
    {
        var list = new List<RestoreVaultRecord>();
        AddFile(list, Path.Combine(_root, "audio-defaults.json"), "Audio", "Audio Defaults", "استعادة الصوت");
        AddFile(list, Path.Combine(_root, "network-gaming-profile.json"), "Network", "Gaming NIC Profile", "استعادة الشبكة");
        AddFile(list, Path.Combine(_root, "power-plan.json"), "Power", "Original Power Plan", "استعادة الطاقة");

        var drivers = Path.Combine(_root, "Drivers");
        if (Directory.Exists(drivers))
        {
            foreach (var folder in Directory.EnumerateDirectories(drivers).OrderByDescending(x => x))
            {
                var info = new DirectoryInfo(folder);
                var size = SafeDirectorySize(folder);
                list.Add(new RestoreVaultRecord("Drivers", info.Name, folder, info.LastWriteTime, size, "استعادة تعريفات Backup"));
            }
        }

        foreach (var file in Directory.EnumerateFiles(_root, "*.json", SearchOption.TopDirectoryOnly))
        {
            if (list.Any(x => x.Path.Equals(file, StringComparison.OrdinalIgnoreCase))) continue;
            var info = new FileInfo(file);
            list.Add(new RestoreVaultRecord("State", Path.GetFileNameWithoutExtension(file), file, info.LastWriteTime, info.Length, "عرض فقط"));
        }

        return list.OrderByDescending(x => x.LastWriteTime).ToArray();
    }

    public async Task<RestoreVaultActionResult> RestoreAsync(RestoreVaultRecord record, CancellationToken cancellationToken = default)
    {
        try
        {
            switch (record.Type)
            {
                case "Audio":
                    return FromText(_audio.RestoreDefaults());
                case "Network":
                {
                    var r = await _network.RestoreAsync(cancellationToken);
                    return new RestoreVaultActionResult(r.Success, r.Detail);
                }
                case "Power":
                {
                    var r = await _power.RestoreAsync(cancellationToken);
                    return new RestoreVaultActionResult(r.Success, r.Detail);
                }
                case "Drivers":
                {
                    var r = await _drivers.RestoreExportedDriversAsync(record.Path, cancellationToken);
                    return new RestoreVaultActionResult(r.Success, r.Detail);
                }
                default:
                    return new RestoreVaultActionResult(false, "هذا العنصر للعرض/السجل فقط ولا يملك Restore handler آمنًا.");
            }
        }
        catch (Exception ex)
        {
            return new RestoreVaultActionResult(false, "Restore Vault: " + ex.Message);
        }
    }

    public string Reveal(RestoreVaultRecord record)
    {
        var path = Directory.Exists(record.Path) ? record.Path : File.Exists(record.Path) ? record.Path : _root;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"",
            UseShellExecute = true
        });
        return "تم فتح موقع النسخة.";
    }

    private static RestoreVaultActionResult FromText(string text)
    {
        var success = !text.StartsWith("فشل", StringComparison.OrdinalIgnoreCase) &&
                      !text.StartsWith("تعذر", StringComparison.OrdinalIgnoreCase) &&
                      !text.StartsWith("لا توجد", StringComparison.OrdinalIgnoreCase);
        return new RestoreVaultActionResult(success, text);
    }

    private static void AddFile(List<RestoreVaultRecord> list, string path, string type, string name, string action)
    {
        if (!File.Exists(path)) return;
        var info = new FileInfo(path);
        list.Add(new RestoreVaultRecord(type, name, path, info.LastWriteTime, info.Length, action));
    }

    private static long SafeDirectorySize(string folder)
    {
        try { return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Sum(x => new FileInfo(x).Length); }
        catch { return 0; }
    }
}
