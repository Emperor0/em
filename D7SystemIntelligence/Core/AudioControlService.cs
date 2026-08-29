using NAudio.CoreAudioApi;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace D7SystemIntelligence.Core;

public sealed record AudioEndpointRecord(
    string Id,
    string Direction,
    string Name,
    bool IsDefaultConsole,
    bool IsDefaultMultimedia,
    bool IsDefaultCommunications,
    float VolumePercent,
    bool Muted,
    int SampleRate,
    int Channels,
    int BitsPerSample);

public sealed record AudioDefaultSnapshot(
    string? RenderConsole,
    string? RenderMultimedia,
    string? RenderCommunications,
    string? CaptureConsole,
    string? CaptureMultimedia,
    string? CaptureCommunications,
    DateTime SavedAtUtc);

public sealed class AudioControlService
{
    private readonly string _backupPath;

    public AudioControlService()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D7SystemIntelligence", "RestoreVault");
        Directory.CreateDirectory(root);
        _backupPath = Path.Combine(root, "audio-defaults.json");
    }

    public IReadOnlyList<AudioEndpointRecord> Scan()
    {
        using var enumerator = new MMDeviceEnumerator();
        var defaults = ReadDefaults(enumerator);
        var result = new List<AudioEndpointRecord>();

        AddFlow(enumerator, DataFlow.Render, "إخراج", defaults, result);
        AddFlow(enumerator, DataFlow.Capture, "إدخال", defaults, result);

        return result
            .OrderBy(x => x.Direction)
            .ThenByDescending(x => x.IsDefaultMultimedia || x.IsDefaultCommunications)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string SetVolume(string deviceId, float percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDevice(deviceId);
        device.AudioEndpointVolume.MasterVolumeLevelScalar = percent / 100f;
        return $"تم ضبط {device.FriendlyName} إلى {percent:0}% فعليًا.";
    }

    public string SetMute(string deviceId, bool muted)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDevice(deviceId);
        device.AudioEndpointVolume.Mute = muted;
        return muted ? $"تم كتم {device.FriendlyName}." : $"تم إلغاء كتم {device.FriendlyName}.";
    }

    public string SetDefault(string deviceId, bool communicationsOnly = false)
    {
        SaveDefaultsIfNeeded();
        using var policy = new PolicyConfigClient();
        if (communicationsOnly)
        {
            policy.SetDefaultEndpoint(deviceId, ERole.eCommunications);
            return "تم تعيين الجهاز كافتراضي للمحادثات Communications.";
        }

        policy.SetDefaultEndpoint(deviceId, ERole.eConsole);
        policy.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
        return "تم تعيين الجهاز كافتراضي لـGame/Desktop (Console + Multimedia).";
    }

    public string SaveCurrentDefaults()
    {
        using var enumerator = new MMDeviceEnumerator();
        var d = ReadDefaults(enumerator);
        var snapshot = new AudioDefaultSnapshot(
            d.RenderConsole, d.RenderMultimedia, d.RenderCommunications,
            d.CaptureConsole, d.CaptureMultimedia, d.CaptureCommunications,
            DateTime.UtcNow);
        File.WriteAllText(_backupPath, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        return "تم حفظ مخارج ومداخل الصوت الافتراضية في Restore Vault.";
    }

    public string RestoreDefaults()
    {
        if (!File.Exists(_backupPath)) return "لا توجد نسخة صوت محفوظة في Restore Vault.";
        AudioDefaultSnapshot? snapshot;
        try { snapshot = JsonSerializer.Deserialize<AudioDefaultSnapshot>(File.ReadAllText(_backupPath)); }
        catch (Exception ex) { return "تعذر قراءة نسخة الصوت: " + ex.Message; }
        if (snapshot == null) return "نسخة الصوت المحفوظة غير صالحة.";

        using var policy = new PolicyConfigClient();
        var restored = 0;
        restored += TrySet(policy, snapshot.RenderConsole, ERole.eConsole);
        restored += TrySet(policy, snapshot.RenderMultimedia, ERole.eMultimedia);
        restored += TrySet(policy, snapshot.RenderCommunications, ERole.eCommunications);
        restored += TrySet(policy, snapshot.CaptureConsole, ERole.eConsole);
        restored += TrySet(policy, snapshot.CaptureMultimedia, ERole.eMultimedia);
        restored += TrySet(policy, snapshot.CaptureCommunications, ERole.eCommunications);
        return $"تمت محاولة استعادة {restored} تعيينات Default Audio محفوظة. أعد الفحص للتأكد من النتيجة.";
    }

    private void SaveDefaultsIfNeeded()
    {
        if (!File.Exists(_backupPath)) SaveCurrentDefaults();
    }

    private static int TrySet(PolicyConfigClient policy, string? id, ERole role)
    {
        if (string.IsNullOrWhiteSpace(id)) return 0;
        try { policy.SetDefaultEndpoint(id, role); return 1; }
        catch { return 0; }
    }

    private static void AddFlow(
        MMDeviceEnumerator enumerator,
        DataFlow flow,
        string direction,
        DefaultIds defaults,
        List<AudioEndpointRecord> result)
    {
        var devices = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
        foreach (var device in devices)
        {
            using (device)
            {
                try
                {
                    var volume = device.AudioEndpointVolume.MasterVolumeLevelScalar * 100f;
                    var muted = device.AudioEndpointVolume.Mute;
                    var format = device.AudioClient.MixFormat;
                    var id = device.ID;
                    result.Add(new AudioEndpointRecord(
                        id,
                        direction,
                        device.FriendlyName,
                        id.Equals(flow == DataFlow.Render ? defaults.RenderConsole : defaults.CaptureConsole, StringComparison.OrdinalIgnoreCase),
                        id.Equals(flow == DataFlow.Render ? defaults.RenderMultimedia : defaults.CaptureMultimedia, StringComparison.OrdinalIgnoreCase),
                        id.Equals(flow == DataFlow.Render ? defaults.RenderCommunications : defaults.CaptureCommunications, StringComparison.OrdinalIgnoreCase),
                        volume,
                        muted,
                        format.SampleRate,
                        format.Channels,
                        format.BitsPerSample));
                }
                catch
                {
                    result.Add(new AudioEndpointRecord(
                        device.ID, direction, device.FriendlyName,
                        false, false, false, 0, false, 0, 0, 0));
                }
            }
        }
    }

    private static DefaultIds ReadDefaults(MMDeviceEnumerator enumerator)
        => new(
            GetDefaultId(enumerator, DataFlow.Render, Role.Console),
            GetDefaultId(enumerator, DataFlow.Render, Role.Multimedia),
            GetDefaultId(enumerator, DataFlow.Render, Role.Communications),
            GetDefaultId(enumerator, DataFlow.Capture, Role.Console),
            GetDefaultId(enumerator, DataFlow.Capture, Role.Multimedia),
            GetDefaultId(enumerator, DataFlow.Capture, Role.Communications));

    private static string? GetDefaultId(MMDeviceEnumerator enumerator, DataFlow flow, Role role)
    {
        try
        {
            using var d = enumerator.GetDefaultAudioEndpoint(flow, role);
            return d.ID;
        }
        catch { return null; }
    }

    private sealed record DefaultIds(
        string? RenderConsole,
        string? RenderMultimedia,
        string? RenderCommunications,
        string? CaptureConsole,
        string? CaptureMultimedia,
        string? CaptureCommunications);
}

internal enum ERole
{
    eConsole = 0,
    eMultimedia = 1,
    eCommunications = 2
}

internal sealed class PolicyConfigClient : IDisposable
{
    private readonly IPolicyConfigVista _policy;

    public PolicyConfigClient()
    {
        var type = Type.GetTypeFromCLSID(new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"), true)
                   ?? throw new InvalidOperationException("PolicyConfig COM غير متاح على Windows.");
        _policy = (IPolicyConfigVista)Activator.CreateInstance(type)!;
    }

    public void SetDefaultEndpoint(string deviceId, ERole role)
    {
        var hr = _policy.SetDefaultEndpoint(deviceId, role);
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);
    }

    public void Dispose()
    {
        try { Marshal.FinalReleaseComObject(_policy); } catch { }
    }
}

[ComImport]
[Guid("568B9108-44BF-40B4-9006-86AFE5B5A620")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfigVista
{
    [PreserveSig] int GetMixFormat();
    [PreserveSig] int GetDeviceFormat();
    [PreserveSig] int ResetDeviceFormat();
    [PreserveSig] int SetDeviceFormat();
    [PreserveSig] int GetProcessingPeriod();
    [PreserveSig] int SetProcessingPeriod();
    [PreserveSig] int GetShareMode();
    [PreserveSig] int SetShareMode();
    [PreserveSig] int GetPropertyValue();
    [PreserveSig] int SetPropertyValue();
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, ERole role);
    [PreserveSig] int SetEndpointVisibility();
}
