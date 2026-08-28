using System.Diagnostics;
using System.Text.Json;

namespace D7SystemIntelligence.Core;

public sealed class PeripheralIntelligence
{
    public async Task<List<PeripheralRecord>> ScanAsync()
    {
        const string script = "Get-PnpDevice -PresentOnly | Where-Object {$_.Class -in @('Mouse','Keyboard','HIDClass','Bluetooth','Monitor','AudioEndpoint')} | Select-Object Class,FriendlyName,Status,InstanceId | ConvertTo-Json -Depth 3 -Compress";
        var json = await RunPowerShellAsync(script);
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            using var doc = JsonDocument.Parse(json);
            var list = new List<PeripheralRecord>();
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in doc.RootElement.EnumerateArray()) Add(e, list);
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                Add(doc.RootElement, list);
            }

            return list
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.InstanceId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(x => x.Category)
                .ThenBy(x => x.Name)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static void Add(JsonElement e, List<PeripheralRecord> list)
    {
        static string Read(JsonElement el, string name)
            => el.TryGetProperty(name, out var p) && p.ValueKind != JsonValueKind.Null ? p.ToString() : string.Empty;

        var cls = Read(e, "Class");
        var name = Read(e, "FriendlyName");
        var status = Read(e, "Status");
        var id = Read(e, "InstanceId");
        list.Add(new PeripheralRecord(ToArabicCategory(cls), name, status, id));
    }

    private static string ToArabicCategory(string cls) => cls.ToLowerInvariant() switch
    {
        "mouse" => "ماوس",
        "keyboard" => "كيبورد",
        "monitor" => "شاشة",
        "bluetooth" => "بلوتوث",
        "audioendpoint" => "صوت",
        "hidclass" => "HID / يد تحكم",
        _ => cls
    };

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
