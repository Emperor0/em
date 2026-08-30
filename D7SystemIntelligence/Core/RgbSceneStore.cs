using System.Text.Json;

namespace D7SystemIntelligence.Core;

public sealed class D7RgbDeviceScene
{
    public int DeviceIndex { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string Mode { get; set; } = "static";
    public string Color { get; set; } = "FF0000";
    public int Brightness { get; set; } = 100;
    public bool Enabled { get; set; } = true;
}

public sealed class D7RgbScene
{
    public string Name { get; set; } = "Scene";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public List<D7RgbDeviceScene> Devices { get; set; } = [];
}

public sealed class RgbSceneStore
{
    private readonly string _root;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public RgbSceneStore()
    {
        _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "D7SystemIntelligence",
            "RgbScenes");
        Directory.CreateDirectory(_root);
    }

    public IReadOnlyList<string> List()
        => Directory.EnumerateFiles(_root, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();

    public void Save(D7RgbScene scene)
    {
        scene.Name = NormalizeName(scene.Name);
        if (string.IsNullOrWhiteSpace(scene.Name))
            throw new InvalidOperationException("اسم Scene غير صالح.");
        scene.UpdatedAt = DateTimeOffset.Now;
        foreach (var device in scene.Devices)
        {
            device.Brightness = Math.Clamp(device.Brightness, 0, 100);
            device.Color = NormalizeColor(device.Color);
            if (string.IsNullOrWhiteSpace(device.Mode)) device.Mode = "static";
        }
        File.WriteAllText(Path.Combine(_root, scene.Name + ".json"), JsonSerializer.Serialize(scene, Options));
    }

    public D7RgbScene? Load(string name)
    {
        var safe = NormalizeName(name);
        var path = Path.Combine(_root, safe + ".json");
        if (!File.Exists(path)) return null;
        try
        {
            var scene = JsonSerializer.Deserialize<D7RgbScene>(File.ReadAllText(path), Options);
            if (scene == null) return null;
            scene.Name = safe;
            return scene;
        }
        catch { return null; }
    }

    public bool Delete(string name)
    {
        var path = Path.Combine(_root, NormalizeName(name) + ".json");
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    private static string NormalizeName(string? name)
    {
        var value = (name ?? string.Empty).Trim();
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Where(ch => !invalid.Contains(ch))).Trim();
    }

    private static string NormalizeColor(string? value)
    {
        var raw = (value ?? string.Empty).Trim().TrimStart('#').ToUpperInvariant();
        return raw.Length == 6 && raw.All(Uri.IsHexDigit) ? raw : "FFFFFF";
    }
}
