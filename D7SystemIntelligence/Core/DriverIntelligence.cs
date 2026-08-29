using System.Diagnostics;
using System.Text.Json;

namespace D7SystemIntelligence.Core;

public sealed class DriverIntelligence
{
    public async Task<List<DriverRecord>> ScanAsync()
    {
        const string script = "Get-CimInstance Win32_PnPSignedDriver | Where-Object {($_.DeviceClass -in @('DISPLAY','NET','MEDIA','BLUETOOTH')) -or ($_.DeviceClass -eq 'SYSTEM' -and $_.DeviceName -match 'AMD|SMBus|PCI|GPIO|PSP|Chipset') -or ($_.DeviceClass -eq 'USB' -and $_.DeviceName -match 'Host Controller|Root Hub')} | Select-Object DeviceName,DeviceClass,DriverVersion,DriverDate,Manufacturer,InfName | ConvertTo-Json -Depth 3 -Compress";
        var json = await RunPowerShellAsync(script);
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            using var doc = JsonDocument.Parse(json);
            var list = new List<DriverRecord>();
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in doc.RootElement.EnumerateArray()) Add(e, list);
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                Add(doc.RootElement, list);
            }

            return list
                .Where(x => !string.IsNullOrWhiteSpace(x.DeviceName))
                .GroupBy(x => $"{x.DeviceName}|{x.InfName}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(x => Rank(x.DeviceClass))
                .ThenBy(x => x.DeviceName)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public static string BuildSummary(IReadOnlyList<DriverRecord> drivers)
    {
        var display = drivers.FirstOrDefault(d => d.DeviceClass.Equals("DISPLAY", StringComparison.OrdinalIgnoreCase));
        var net = drivers.Count(d => d.DeviceClass.Equals("NET", StringComparison.OrdinalIgnoreCase));
        var audio = drivers.Count(d => d.DeviceClass.Equals("MEDIA", StringComparison.OrdinalIgnoreCase));
        if (display == null) return $"تمت قراءة {drivers.Count} تعريفًا مهمًا • شبكة {net} • صوت {audio}";
        return $"GPU: {display.DeviceName} • الإصدار {display.DriverVersion} • شبكة {net} • صوت {audio} • إجمالي مهم {drivers.Count}";
    }

    private static int Rank(string cls) => cls.ToUpperInvariant() switch
    {
        "DISPLAY" => 0,
        "NET" => 1,
        "MEDIA" => 2,
        "BLUETOOTH" => 3,
        "SYSTEM" => 4,
        "USB" => 5,
        _ => 9
    };

    private static void Add(JsonElement e, List<DriverRecord> list)
    {
        static string Read(JsonElement el, string name)
            => el.TryGetProperty(name, out var p) && p.ValueKind != JsonValueKind.Null ? p.ToString() : string.Empty;

        list.Add(new DriverRecord(
            Read(e, "DeviceName"),
            Read(e, "DeviceClass"),
            Read(e, "DriverVersion"),
            Read(e, "DriverDate"),
            Read(e, "Manufacturer"),
            Read(e, "InfName")));
    }

    private static async Task<string> RunPowerShellAsync(string script)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return string.Empty;
            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return output.Trim();
        }
        catch
        {
            return string.Empty;
        }
    }
}
