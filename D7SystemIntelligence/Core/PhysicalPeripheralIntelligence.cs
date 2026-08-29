using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace D7SystemIntelligence.Core;

public sealed record PhysicalPeripheralRecord(
    string Category,
    string Name,
    string Status,
    string Transport,
    string ContainerId,
    string ParentId,
    string Location,
    int InterfaceCount,
    IReadOnlyList<string> InterfaceIds);

public sealed class PhysicalPeripheralIntelligence
{
    public async Task<List<PhysicalPeripheralRecord>> ScanAsync(CancellationToken cancellationToken = default)
    {
        const string script = @"
$classes = @('Mouse','Keyboard','HIDClass','Bluetooth','Monitor','AudioEndpoint')
$items = Get-PnpDevice -PresentOnly | Where-Object { $_.Class -in $classes } | ForEach-Object {
    $id = $_.InstanceId
    $container = (Get-PnpDeviceProperty -InstanceId $id -KeyName 'DEVPKEY_Device_ContainerId' -ErrorAction SilentlyContinue).Data
    $parent = (Get-PnpDeviceProperty -InstanceId $id -KeyName 'DEVPKEY_Device_Parent' -ErrorAction SilentlyContinue).Data
    $bus = (Get-PnpDeviceProperty -InstanceId $id -KeyName 'DEVPKEY_Device_BusReportedDeviceDesc' -ErrorAction SilentlyContinue).Data
    $location = (Get-PnpDeviceProperty -InstanceId $id -KeyName 'DEVPKEY_Device_LocationPaths' -ErrorAction SilentlyContinue).Data
    [pscustomobject]@{
        Class = $_.Class
        FriendlyName = $_.FriendlyName
        Status = $_.Status
        InstanceId = $id
        ContainerId = if($container){$container.ToString()}else{''}
        ParentId = if($parent){$parent.ToString()}else{''}
        BusName = if($bus){$bus.ToString()}else{''}
        Location = if($location){($location -join '; ')}else{''}
    }
}
$items | ConvertTo-Json -Depth 4 -Compress
";

        var json = await RunPowerShellAsync(script, cancellationToken);
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            using var doc = JsonDocument.Parse(json);
            var raw = new List<RawPeripheral>();
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in doc.RootElement.EnumerateArray()) raw.Add(Parse(e));
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                raw.Add(Parse(doc.RootElement));
            }

            var groups = raw
                .Where(x => !string.IsNullOrWhiteSpace(x.InstanceId))
                .GroupBy(BuildPhysicalKey, StringComparer.OrdinalIgnoreCase);

            var result = new List<PhysicalPeripheralRecord>();
            foreach (var group in groups)
            {
                var members = group.ToList();
                var name = PickBestName(members);
                var category = PickCategory(members, name);
                var status = members.All(x => x.Status.Equals("OK", StringComparison.OrdinalIgnoreCase)) ? "OK" :
                    string.Join(" / ", members.Select(x => x.Status).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase));
                var first = members[0];
                var transport = DetectTransport(members);
                var location = members.Select(x => x.Location).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
                result.Add(new PhysicalPeripheralRecord(
                    category,
                    name,
                    string.IsNullOrWhiteSpace(status) ? "Unknown" : status,
                    transport,
                    first.ContainerId,
                    first.ParentId,
                    location,
                    members.Count,
                    members.Select(x => x.InstanceId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
            }

            return result
                .OrderBy(x => CategoryRank(x.Category))
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static RawPeripheral Parse(JsonElement e)
    {
        static string Read(JsonElement el, string name)
            => el.TryGetProperty(name, out var p) && p.ValueKind != JsonValueKind.Null ? p.ToString() : string.Empty;

        return new RawPeripheral(
            Read(e, "Class"), Read(e, "FriendlyName"), Read(e, "Status"), Read(e, "InstanceId"),
            Read(e, "ContainerId"), Read(e, "ParentId"), Read(e, "BusName"), Read(e, "Location"));
    }

    private static string BuildPhysicalKey(RawPeripheral item)
    {
        if (!string.IsNullOrWhiteSpace(item.ContainerId) &&
            !item.ContainerId.Equals("00000000-0000-0000-ffff-ffffffffffff", StringComparison.OrdinalIgnoreCase))
            return "C:" + item.ContainerId;

        if (!string.IsNullOrWhiteSpace(item.ParentId)) return "P:" + item.ParentId;

        var id = item.InstanceId;
        var idx = id.LastIndexOf('&');
        return idx > 0 ? "I:" + id[..idx] : "I:" + id;
    }

    private static string PickBestName(List<RawPeripheral> members)
    {
        var candidates = members
            .SelectMany(x => new[] { x.BusName, x.Name })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(NamePenalty)
            .ThenByDescending(x => x.Length)
            .ToList();

        return candidates.FirstOrDefault() ?? "جهاز غير مسمى";
    }

    private static int NamePenalty(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("composite") || n.Contains("input device") || n.Contains("hid-compliant") || n.Contains("generic")) return 100;
        if (n.Contains("consumer control") || n.Contains("system control") || n.Contains("vendor-defined")) return 90;
        if (n.Contains("usb receiver")) return 50;
        return 0;
    }

    private static string PickCategory(List<RawPeripheral> members, string name)
    {
        var text = (name + " " + string.Join(' ', members.Select(x => x.Class))).ToLowerInvariant();
        if (text.Contains("xbox") || text.Contains("controller") || text.Contains("gamepad") || text.Contains("dualsense") || text.Contains("dualshock")) return "يد تحكم";
        if (members.Any(x => x.Class.Equals("Mouse", StringComparison.OrdinalIgnoreCase))) return "ماوس";
        if (members.Any(x => x.Class.Equals("Keyboard", StringComparison.OrdinalIgnoreCase))) return "كيبورد";
        if (members.Any(x => x.Class.Equals("Monitor", StringComparison.OrdinalIgnoreCase))) return "شاشة";
        if (members.Any(x => x.Class.Equals("AudioEndpoint", StringComparison.OrdinalIgnoreCase))) return "صوت";
        if (members.Any(x => x.Class.Equals("Bluetooth", StringComparison.OrdinalIgnoreCase))) return "بلوتوث";
        return "HID";
    }

    private static string DetectTransport(List<RawPeripheral> members)
    {
        var ids = string.Join(' ', members.Select(x => x.InstanceId)).ToUpperInvariant();
        var loc = string.Join(' ', members.Select(x => x.Location)).ToUpperInvariant();
        if (ids.Contains("BTH") || ids.Contains("BLUETOOTH")) return "Bluetooth";
        if (ids.Contains("USB") || loc.Contains("USB")) return "USB";
        if (ids.Contains("HID")) return "HID";
        return "غير معروف";
    }

    private static int CategoryRank(string category) => category switch
    {
        "ماوس" => 0,
        "كيبورد" => 1,
        "يد تحكم" => 2,
        "صوت" => 3,
        "شاشة" => 4,
        "بلوتوث" => 5,
        _ => 6
    };

    private static async Task<string> RunPowerShellAsync(string script, CancellationToken cancellationToken)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var p = Process.Start(psi);
        if (p == null) return string.Empty;
        var outputTask = p.StandardOutput.ReadToEndAsync(cancellationToken);
        await p.WaitForExitAsync(cancellationToken);
        return (await outputTask).Trim();
    }

    private sealed record RawPeripheral(
        string Class,
        string Name,
        string Status,
        string InstanceId,
        string ContainerId,
        string ParentId,
        string BusName,
        string Location);
}
