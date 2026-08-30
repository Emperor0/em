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
        return result.OrderBy(x => x.Direction)
            .ThenByDescending(x => x.IsDefaultMultimedia || x.IsDefaultCommunications)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string SetVolume(string deviceId, float percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDevice(deviceId);
        var before = device.AudioEndpointVolume.MasterVolumeLevelScalar * 100f;
        if (Math.Abs(before - percent) < .5f) return $"Already optimal • {device.FriendlyName} أصلًا {before:0}%؛ لم يتغير شيء.";
        device.AudioEndpointVolume.MasterVolumeLevelScalar = percent / 100f;
        Thread.Sleep(80);
        var verified = device.AudioEndpointVolume.MasterVolumeLevelScalar * 100f;
        return Math.Abs(verified - percent) <= 1f
            ? $"Applied + Verified • {device.FriendlyName}: {before:0}% → {verified:0}%."
            : $"تعذر إثبات Volume المطلوبة. القراءة بعد التطبيق {verified:0}% بدل {percent:0}%.";
    }

    public string SetMute(string deviceId, bool muted)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDevice(deviceId);
        var before = device.AudioEndpointVolume.Mute;
        if (before == muted) return $"Already optimal • {device.FriendlyName} {(muted ? "مكتوم" : "غير مكتوم")} أصلًا.";
        device.AudioEndpointVolume.Mute = muted;
        Thread.Sleep(60);
        var verified = device.AudioEndpointVolume.Mute;
        return verified == muted
            ? $"Applied + Verified • {device.FriendlyName}: Mute {(before ? "ON" : "OFF")} → {(verified ? "ON" : "OFF")}."
            : "Windows قبل الأمر لكن القراءة بعد التطبيق لم تثبت حالة Mute المطلوبة.";
    }

    public string SetDefault(string deviceId, bool communicationsOnly = false)
    {
        SaveDefaultsIfNeeded();
        using var enumerator = new MMDeviceEnumerator();
        using var target = enumerator.GetDevice(deviceId);
        using var policy = new PolicyConfigClient();

        if (communicationsOnly)
        {
            var current = IsDefault(enumerator, deviceId, DataFlow.Render, Role.Communications) || IsDefault(enumerator, deviceId, DataFlow.Capture, Role.Communications);
            if (current) return $"Already optimal • {target.FriendlyName} هو Communications Default أصلًا.";
            policy.SetDefaultEndpoint(deviceId, ERole.eCommunications);
            Thread.Sleep(120);
            using var verifyEnum = new MMDeviceEnumerator();
            var verified = IsDefaultAnyFlow(verifyEnum, deviceId, Role.Communications);
            return verified
                ? $"Applied + Verified • {target.FriendlyName} أصبح Communications Default."
                : "PolicyConfig رجع نجاح لكن D7KT لم يثبت أن Communications Default تغير.";
        }

        var alreadyConsole = IsDefaultAnyFlow(enumerator, deviceId, Role.Console);
        var alreadyMultimedia = IsDefaultAnyFlow(enumerator, deviceId, Role.Multimedia);
        if (alreadyConsole && alreadyMultimedia) return $"Already optimal • {target.FriendlyName} هو Game/Desktop Default أصلًا.";

        policy.SetDefaultEndpoint(deviceId, ERole.eConsole);
        policy.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
        Thread.Sleep(150);
        using var verify = new MMDeviceEnumerator();
        var ok = IsDefaultAnyFlow(verify, deviceId, Role.Console) && IsDefaultAnyFlow(verify, deviceId, Role.Multimedia);
        return ok
            ? $"Applied + Verified • {target.FriendlyName} أصبح Console + Multimedia Default. Restore Vault محفوظ."
            : "تم إرسال Default Audio لكن القراءة بعد التطبيق لم تثبت التعيينين؛ Restore Vault محفوظ.";
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
        var requested = 0;
        requested += TrySet(policy, snapshot.RenderConsole, ERole.eConsole);
        requested += TrySet(policy, snapshot.RenderMultimedia, ERole.eMultimedia);
        requested += TrySet(policy, snapshot.RenderCommunications, ERole.eCommunications);
        requested += TrySet(policy, snapshot.CaptureConsole, ERole.eConsole);
        requested += TrySet(policy, snapshot.CaptureMultimedia, ERole.eMultimedia);
        requested += TrySet(policy, snapshot.CaptureCommunications, ERole.eCommunications);
        Thread.Sleep(180);

        using var enumerator = new MMDeviceEnumerator();
        var current = ReadDefaults(enumerator);
        var checks = new[]
        {
            Same(snapshot.RenderConsole, current.RenderConsole),
            Same(snapshot.RenderMultimedia, current.RenderMultimedia),
            Same(snapshot.RenderCommunications, current.RenderCommunications),
            Same(snapshot.CaptureConsole, current.CaptureConsole),
            Same(snapshot.CaptureMultimedia, current.CaptureMultimedia),
            Same(snapshot.CaptureCommunications, current.CaptureCommunications)
        };
        var expected = new[]
        {
            snapshot.RenderConsole, snapshot.RenderMultimedia, snapshot.RenderCommunications,
            snapshot.CaptureConsole, snapshot.CaptureMultimedia, snapshot.CaptureCommunications
        }.Count(x => !string.IsNullOrWhiteSpace(x));
        var verified = checks.Zip(new[]
        {
            snapshot.RenderConsole, snapshot.RenderMultimedia, snapshot.RenderCommunications,
            snapshot.CaptureConsole, snapshot.CaptureMultimedia, snapshot.CaptureCommunications
        }, (ok, id) => string.IsNullOrWhiteSpace(id) || ok).Count(x => x);
        var allOk = verified == 6;

        if (allOk)
        {
            try { File.Delete(_backupPath); } catch { }
            return $"Restore + Verified • {expected} Audio default roles عادت للقيم المحفوظة. تم إغلاق Restore snapshot.";
        }
        return $"Restore غير مكتمل • أرسل D7KT {requested} تعيينات لكن بعض Default roles لم تتطابق بعد القراءة. احتفظ Restore Vault بالنسخة.";
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

    private static bool IsDefaultAnyFlow(MMDeviceEnumerator enumerator, string id, Role role)
        => IsDefault(enumerator, id, DataFlow.Render, role) || IsDefault(enumerator, id, DataFlow.Capture, role);

    private static bool IsDefault(MMDeviceEnumerator enumerator, string id, DataFlow flow, Role role)
    {
        try
        {
            using var d = enumerator.GetDefaultAudioEndpoint(flow, role);
            return d.ID.Equals(id, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool Same(string? expected, string? actual)
        => string.IsNullOrWhiteSpace(expected) || string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);

    private static void AddFlow(MMDeviceEnumerator enumerator, DataFlow flow, string direction, DefaultIds defaults, List<AudioEndpointRecord> result)
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
                        id, direction, device.FriendlyName,
                        id.Equals(flow == DataFlow.Render ? defaults.RenderConsole : defaults.CaptureConsole, StringComparison.OrdinalIgnoreCase),
                        id.Equals(flow == DataFlow.Render ? defaults.RenderMultimedia : defaults.CaptureMultimedia, StringComparison.OrdinalIgnoreCase),
                        id.Equals(flow == DataFlow.Render ? defaults.RenderCommunications : defaults.CaptureCommunications, StringComparison.OrdinalIgnoreCase),
                        volume, muted, format.SampleRate, format.Channels, format.BitsPerSample));
                }
                catch
                {
                    result.Add(new AudioEndpointRecord(device.ID, direction, device.FriendlyName, false, false, false, 0, false, 0, 0, 0));
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
        try { using var d = enumerator.GetDefaultAudioEndpoint(flow, role); return d.ID; }
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

internal enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

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
    public void Dispose() { try { Marshal.FinalReleaseComObject(_policy); } catch { } }
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
