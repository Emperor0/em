using System.Diagnostics;

namespace D7SystemIntelligence.Core;

public sealed record ShadowCaptureStatus(
    bool ObsRunning,
    bool Connected,
    bool ReplayActive,
    string OutputMode,
    string? Encoder,
    string SaveFolder,
    int ReplaySeconds,
    string Detail);

public sealed class ShadowCaptureService : IAsyncDisposable
{
    public const string ObsCredentialTarget = "D7SystemIntelligence/OBSWebSocket";

    private readonly ShadowCaptureSettingsStore _settingsStore = new();
    private ObsWebSocketClient? _obs;

    public ShadowCaptureSettings LoadSettings() => _settingsStore.Load();

    public void SaveSettings(ShadowCaptureSettings settings, string? obsPassword = null)
    {
        _settingsStore.Save(settings);
        if (obsPassword != null)
            WindowsCredentialStore.Save(ObsCredentialTarget, obsPassword);
    }

    public async Task<ShadowCaptureStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var settings = LoadSettings();
        try
        {
            await EnsureConnectedAsync(settings, cancellationToken);
            var mode = await GetOutputModeAsync(cancellationToken);
            var section = IsAdvanced(mode) ? "AdvOut" : "SimpleOutput";
            var encoder = await _obs!.GetProfileParameterAsync(section, "RecEncoder", cancellationToken);
            var active = await _obs.IsReplayBufferActiveAsync(cancellationToken);
            return new ShadowCaptureStatus(
                true,
                true,
                active,
                mode,
                encoder,
                settings.SaveFolder,
                settings.ReplaySeconds,
                active ? "D7 Shadow Capture جاهز ويحفظ من Replay Buffer الفعلي في OBS." : "OBS متصل، لكن Replay Buffer متوقف.");
        }
        catch (Exception ex)
        {
            return new ShadowCaptureStatus(
                IsObsRunning(),
                false,
                false,
                string.Empty,
                null,
                settings.SaveFolder,
                settings.ReplaySeconds,
                FriendlyConnectionError(ex));
        }
    }

    public async Task<string> StartAsync(CancellationToken cancellationToken = default)
    {
        var settings = LoadSettings();
        Directory.CreateDirectory(settings.SaveFolder);
        await EnsureConnectedAsync(settings, cancellationToken);

        var wasActive = await _obs!.IsReplayBufferActiveAsync(cancellationToken);
        if (wasActive)
            await _obs.StopReplayBufferAsync(cancellationToken);

        var mode = await GetOutputModeAsync(cancellationToken);
        var section = IsAdvanced(mode) ? "AdvOut" : "SimpleOutput";

        // OBS uses these exact profile keys for its real in-memory replay buffer.
        await _obs.SetProfileParameterAsync(section, "RecRB", "true", cancellationToken);
        await _obs.SetProfileParameterAsync(section, "RecRBTime", settings.ReplaySeconds.ToString(), cancellationToken);

        // Keep the memory cap comfortably above the requested time/bitrate while still bounded.
        var estimatedMb = Math.Max(512, (int)Math.Ceiling(settings.BitrateMbps * settings.ReplaySeconds / 8.0 * 1.35));
        await _obs.SetProfileParameterAsync(section, "RecRBSize", estimatedMb.ToString(), cancellationToken);
        await _obs.SetProfileParameterAsync(section, "RecRBPrefix", "D7 Replay", cancellationToken);
        await _obs.SetRecordDirectoryAsync(settings.SaveFolder, cancellationToken);

        await _obs.StartReplayBufferAsync(cancellationToken);
        settings.Enabled = true;
        _settingsStore.Save(settings);

        var encoder = await _obs.GetProfileParameterAsync(section, "RecEncoder", cancellationToken);
        return $"تم تشغيل D7 Shadow Capture فعليًا عبر OBS Replay Buffer. المدة: {settings.ReplaySeconds} ثانية • المجلد: {settings.SaveFolder}" +
               (string.IsNullOrWhiteSpace(encoder) ? string.Empty : $" • Encoder: {encoder}");
    }

    public async Task<string> StopAsync(CancellationToken cancellationToken = default)
    {
        var settings = LoadSettings();
        await EnsureConnectedAsync(settings, cancellationToken);
        if (await _obs!.IsReplayBufferActiveAsync(cancellationToken))
            await _obs.StopReplayBufferAsync(cancellationToken);

        settings.Enabled = false;
        _settingsStore.Save(settings);
        return "تم إيقاف D7 Shadow Capture.";
    }

    public async Task<string> SaveReplayAsync(CancellationToken cancellationToken = default)
    {
        var settings = LoadSettings();
        await EnsureConnectedAsync(settings, cancellationToken);

        if (!await _obs!.IsReplayBufferActiveAsync(cancellationToken))
            throw new InvalidOperationException("Shadow Capture متوقف. شغله أولًا حتى يكون عند D7 مقطع سابق محفوظ في الذاكرة.");

        Directory.CreateDirectory(settings.SaveFolder);
        var before = SnapshotFiles(settings.SaveFolder);
        await _obs.SaveReplayBufferAsync(cancellationToken);

        var saved = await WaitForNewReplayAsync(settings.SaveFolder, before, cancellationToken);
        if (saved == null)
            return $"أرسل D7 أمر حفظ آخر {settings.ReplaySeconds} ثانية إلى OBS بنجاح، لكن لم يتمكن من تحديد اسم الملف خلال مهلة الفحص. افتح {settings.SaveFolder}.";

        ApplyLibraryLimit(settings);
        return $"تم حفظ آخر {settings.ReplaySeconds} ثانية:\n{saved}";
    }

    public async Task<string> ReconfigureAsync(CancellationToken cancellationToken = default)
    {
        var settings = LoadSettings();
        if (!settings.Enabled)
            return "تم حفظ الإعدادات. Shadow Capture غير مفعّل حاليًا.";
        return await StartAsync(cancellationToken);
    }

    private async Task EnsureConnectedAsync(ShadowCaptureSettings settings, CancellationToken cancellationToken)
    {
        if (_obs?.IsConnected == true) return;

        if (!IsObsRunning() && settings.AutoStartObs)
        {
            var path = FindObsExecutable();
            if (path == null)
                throw new InvalidOperationException("OBS Studio غير موجود في المسارات المعروفة. ثبّت OBS أو افتحه يدويًا ثم أعد المحاولة.");

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                Arguments = "--minimize-to-tray",
                WorkingDirectory = Path.GetDirectoryName(path)!,
                UseShellExecute = true
            });

            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline && !IsObsRunning())
                await Task.Delay(250, cancellationToken);
            await Task.Delay(1500, cancellationToken);
        }

        var password = WindowsCredentialStore.Read(ObsCredentialTarget);
        Exception? last = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (_obs != null) await _obs.DisposeAsync();
                _obs = new ObsWebSocketClient();
                await _obs.ConnectAsync(settings.ObsHost, settings.ObsPort, password, cancellationToken);
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(700, cancellationToken);
            }
        }

        throw new InvalidOperationException(FriendlyConnectionError(last ?? new Exception("تعذر الاتصال")), last);
    }

    private async Task<string> GetOutputModeAsync(CancellationToken cancellationToken)
        => await _obs!.GetProfileParameterAsync("Output", "Mode", cancellationToken) ?? "Simple";

    private static bool IsAdvanced(string mode)
        => mode.Contains("Advanced", StringComparison.OrdinalIgnoreCase) || mode.Contains("Adv", StringComparison.OrdinalIgnoreCase);

    private static bool IsObsRunning()
        => Process.GetProcessesByName("obs64").Length > 0 || Process.GetProcessesByName("obs32").Length > 0;

    private static string? FindObsExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "obs-studio", "bin", "64bit", "obs64.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "obs-studio", "bin", "64bit", "obs64.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "obs-studio", "bin", "64bit", "obs64.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static Dictionary<string, (long Length, DateTime LastWriteUtc)> SnapshotFiles(string folder)
    {
        if (!Directory.Exists(folder)) return new(StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(folder)
            .ToDictionary(
                x => x,
                x =>
                {
                    var info = new FileInfo(x);
                    return (info.Length, info.LastWriteTimeUtc);
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<string?> WaitForNewReplayAsync(
        string folder,
        Dictionary<string, (long Length, DateTime LastWriteUtc)> before,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(250, cancellationToken);

            if (!Directory.Exists(folder)) continue;
            var candidate = Directory.EnumerateFiles(folder)
                .Select(x => new FileInfo(x))
                .Where(x => !before.TryGetValue(x.FullName, out var old) || old.Length != x.Length || old.LastWriteUtc != x.LastWriteTimeUtc)
                .OrderByDescending(x => x.LastWriteTimeUtc)
                .FirstOrDefault();

            if (candidate is { Length: > 0 })
                return candidate.FullName;
        }

        return null;
    }

    private static void ApplyLibraryLimit(ShadowCaptureSettings settings)
    {
        if (!settings.AutoCleanup || !Directory.Exists(settings.SaveFolder)) return;
        var maxBytes = settings.MaxLibraryGb * 1024L * 1024L * 1024L;
        var files = Directory.EnumerateFiles(settings.SaveFolder)
            .Select(x => new FileInfo(x))
            .OrderByDescending(x => x.LastWriteTimeUtc)
            .ToList();

        long total = files.Sum(x => x.Length);
        foreach (var file in files.OrderBy(x => x.LastWriteTimeUtc))
        {
            if (total <= maxBytes) break;
            try
            {
                var size = file.Length;
                file.Delete();
                total -= size;
            }
            catch { }
        }
    }

    private static string FriendlyConnectionError(Exception ex)
    {
        var text = ex.Message;
        if (text.Contains("actively refused", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("تعذر إجراء اتصال", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Unable to connect", StringComparison.OrdinalIgnoreCase))
        {
            return "تعذر الاتصال بـ OBS WebSocket على 127.0.0.1:4455. من OBS افتح Tools → WebSocket Server Settings وفعّل Enable WebSocket server، ثم احفظ كلمة المرور في D7.";
        }
        return text;
    }

    public async ValueTask DisposeAsync()
    {
        if (_obs != null)
            await _obs.DisposeAsync();
        _obs = null;
    }
}
