using System.Diagnostics;
using System.Text.Json;

namespace D7SystemIntelligence.Core;

public sealed record ShadowCaptureStatus(
    bool ObsRunning,
    bool Connected,
    bool ReplayActive,
    string OutputMode,
    string? Encoder,
    string SaveFolder,
    int ReplaySeconds,
    string Detail,
    bool StreamActive,
    bool RecordActive,
    double? ObsCpuUsage,
    double? RenderSkipPercent,
    double? OutputSkipPercent,
    double? ObsActiveFps,
    double? GameFps,
    double? GameOnePercentLow,
    double? GameP99FrameMs,
    double? GpuLoad,
    string Health,
    string DuplicateCaptureWarning);

public sealed record CaptureImpactResult(
    bool Performed,
    bool Passed,
    double? BaselineFps,
    double? ReplayFps,
    double? FpsLoss,
    double? BaselineOnePercentLow,
    double? ReplayOnePercentLow,
    double? P99IncreaseMs,
    double? ObservedGpuLoadIncrease,
    double ObsRenderSkipPercent,
    double ObsOutputSkipPercent,
    string Verdict);

public sealed record D7ClipMetadata(
    int SchemaVersion,
    DateTimeOffset SavedAt,
    string Game,
    string Mission,
    int ReplaySeconds,
    string Backend,
    string? Encoder,
    long FileSizeBytes,
    double? CpuLoad,
    double? GpuLoad,
    double? RamLoad,
    double? VramLoad,
    double? CpuTemp,
    double? GpuTemp,
    double? Fps,
    double? OnePercentLow,
    double? P99FrameMs,
    double? ObsRenderSkipPercent,
    double? ObsOutputSkipPercent);

internal sealed record ShadowObsBackup(
    DateTimeOffset CreatedAt,
    string OutputMode,
    string Section,
    string? RecRb,
    string? RecRbTime,
    string? RecRbSize,
    string? RecRbPrefix,
    string? RecordDirectory);

public sealed class ShadowCaptureService : IAsyncDisposable
{
    public const string ObsCredentialTarget = "D7SystemIntelligence/OBSWebSocket";
    private const string D7ReplayPrefix = "D7KT Replay";

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".mov", ".webm", ".flv", ".ts", ".m4v"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly ShadowCaptureSettingsStore _settingsStore = new();
    private readonly string _backupPath;
    private ObsWebSocketClient? _obs;
    private bool _startedReplayByD7;
    private bool _adoptedExistingReplay;

    public ShadowCaptureService()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "D7SystemIntelligence");
        Directory.CreateDirectory(root);
        _backupPath = Path.Combine(root, "shadow-obs-backup.json");
    }

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
            var streaming = await SafeAsync(() => _obs.IsStreamActiveAsync(cancellationToken));
            var recording = await SafeAsync(() => _obs.IsRecordActiveAsync(cancellationToken));
            var stats = await SafeStatsAsync(cancellationToken);
            var sample = D7RuntimeBus.SessionSample;
            var hardware = D7RuntimeBus.Hardware;
            var duplicates = DetectOtherCaptureApps();
            var health = BuildHealth(settings, stats, sample, hardware, duplicates);

            var ownership = active
                ? _startedReplayByD7 ? "Replay بدأه D7KT ويمكن استعادته بالكامل عند الإيقاف."
                : _adoptedExistingReplay ? "D7KT يستخدم Replay الموجود مسبقًا في OBS بدون تغيير إعداداته."
                : "Replay يعمل في OBS؛ D7KT لن يفترض ملكيته أو يوقفه تلقائيًا."
                : "Replay Buffer متوقف.";

            return new ShadowCaptureStatus(
                true,
                true,
                active,
                mode,
                encoder,
                settings.SaveFolder,
                settings.ReplaySeconds,
                $"{ownership} {health.Detail}",
                streaming,
                recording,
                stats?.CpuUsage,
                stats?.RenderSkipPercent,
                stats?.OutputSkipPercent,
                stats?.ActiveFps,
                sample?.Fps,
                sample?.OnePercentLow,
                sample?.P99FrameMs,
                hardware?.GpuLoad,
                health.Level,
                duplicates);
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
                FriendlyConnectionError(ex),
                false,
                false,
                null,
                null,
                null,
                null,
                D7RuntimeBus.SessionSample?.Fps,
                D7RuntimeBus.SessionSample?.OnePercentLow,
                D7RuntimeBus.SessionSample?.P99FrameMs,
                D7RuntimeBus.Hardware?.GpuLoad,
                "Unavailable",
                DetectOtherCaptureApps());
        }
    }

    public async Task<string> StartAsync(CancellationToken cancellationToken = default)
    {
        var settings = LoadSettings();
        Directory.CreateDirectory(settings.SaveFolder);
        await EnsureConnectedAsync(settings, cancellationToken);

        var alreadyActive = await _obs!.IsReplayBufferActiveAsync(cancellationToken);
        if (alreadyActive)
        {
            _adoptedExistingReplay = true;
            _startedReplayByD7 = false;
            settings.Enabled = true;
            _settingsStore.Save(settings);
            var existingDirectory = await SafeStringAsync(() => _obs.GetRecordDirectoryAsync(cancellationToken));
            return "Replay Buffer كان شغال مسبقًا في OBS. D7KT اعتمده كما هو ولم يوقفه أو يغير مدته/Encoder/Mem limit" +
                   (string.IsNullOrWhiteSpace(existingDirectory) ? "." : $". مسار OBS الحالي: {existingDirectory}");
        }

        var backup = await CaptureBackupAsync(cancellationToken);
        SaveBackup(backup);

        try
        {
            var section = backup.Section;
            await _obs.SetProfileParameterAsync(section, "RecRB", "true", cancellationToken);
            await _obs.SetProfileParameterAsync(section, "RecRBTime", settings.ReplaySeconds.ToString(), cancellationToken);

            // This is only a bounded OBS memory ceiling estimate, not a claimed GPU reservation.
            var estimatedMb = Math.Max(256, (int)Math.Ceiling(settings.BitrateMbps * settings.ReplaySeconds / 8.0 * 1.35));
            await _obs.SetProfileParameterAsync(section, "RecRBSize", estimatedMb.ToString(), cancellationToken);
            await _obs.SetProfileParameterAsync(section, "RecRBPrefix", D7ReplayPrefix, cancellationToken);
            await _obs.SetRecordDirectoryAsync(settings.SaveFolder, cancellationToken);
            await _obs.StartReplayBufferAsync(cancellationToken);

            _startedReplayByD7 = true;
            _adoptedExistingReplay = false;
            settings.Enabled = true;
            _settingsStore.Save(settings);

            var encoder = await _obs.GetProfileParameterAsync(section, "RecEncoder", cancellationToken);
            return $"D7KT Shadow Capture يعمل عبر OBS Replay Buffer • {settings.ReplaySeconds}s • {settings.SaveFolder}" +
                   (string.IsNullOrWhiteSpace(encoder) ? string.Empty : $" • Encoder {encoder}") +
                   ". تم حفظ إعدادات OBS السابقة للاستعادة.";
        }
        catch
        {
            try { await RestoreBackupAsync(backup, cancellationToken); } catch { }
            throw;
        }
    }

    public async Task<string> StopAsync(CancellationToken cancellationToken = default)
    {
        var settings = LoadSettings();
        await EnsureConnectedAsync(settings, cancellationToken);
        var active = await _obs!.IsReplayBufferActiveAsync(cancellationToken);
        var backup = LoadBackup();
        var currentPrefix = await GetCurrentReplayPrefixAsync(cancellationToken);
        var ownsPersistedReplay = backup != null && string.Equals(currentPrefix, D7ReplayPrefix, StringComparison.OrdinalIgnoreCase);
        var owns = _startedReplayByD7 || ownsPersistedReplay;

        if (owns)
        {
            if (active) await _obs.StopReplayBufferAsync(cancellationToken);
            if (backup != null) await RestoreBackupAsync(backup, cancellationToken);
            _startedReplayByD7 = false;
            _adoptedExistingReplay = false;
            settings.Enabled = false;
            _settingsStore.Save(settings);
            return "تم إيقاف Replay الذي بدأه D7KT واستعادة إعدادات OBS السابقة ومسار التسجيل.";
        }

        _startedReplayByD7 = false;
        _adoptedExistingReplay = false;
        settings.Enabled = false;
        _settingsStore.Save(settings);
        return active
            ? "تم فصل D7KT عن Shadow Capture، لكن Replay الموجود مسبقًا في OBS تُرك شغالًا ولم يلمسه D7KT."
            : "Shadow Capture متوقف.";
    }

    public async Task<string> SaveReplayAsync(CancellationToken cancellationToken = default)
    {
        var settings = LoadSettings();
        await EnsureConnectedAsync(settings, cancellationToken);

        if (!await _obs!.IsReplayBufferActiveAsync(cancellationToken))
            throw new InvalidOperationException("Shadow Capture متوقف. شغله أولًا حتى يوجد Replay سابق في الذاكرة.");

        var sourceFolder = await SafeStringAsync(() => _obs.GetRecordDirectoryAsync(cancellationToken));
        if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder))
            sourceFolder = settings.SaveFolder;
        Directory.CreateDirectory(sourceFolder);

        var before = SnapshotFiles(sourceFolder);
        await _obs.SaveReplayBufferAsync(cancellationToken);
        var saved = await WaitForNewReplayAsync(sourceFolder, before, cancellationToken);
        if (saved == null)
            return $"OBS قبل أمر حفظ آخر {settings.ReplaySeconds}s، لكن D7KT لم يحدد الملف خلال مهلة التحقق. افحص {sourceFolder}.";

        var organized = await OrganizeSavedReplayAsync(saved, settings, cancellationToken);
        ApplyLibraryLimit(settings);
        return $"تم حفظ آخر {settings.ReplaySeconds} ثانية:\n{organized}";
    }

    public async Task<CaptureImpactResult> RunImpactCheckAsync(CancellationToken cancellationToken = default)
    {
        var settings = LoadSettings();
        var context = D7RuntimeBus.Context;
        var sample = D7RuntimeBus.SessionSample;
        if (context?.PrimaryGame == null || sample?.Fps == null)
        {
            return new CaptureImpactResult(false, false, null, null, null, null, null, null, null, 0, 0,
                "افتح لعبة وانتظر Stutter Black Box/PresentMon حتى تظهر FPS فعلية. D7KT لن يخمن تأثير التسجيل بدون baseline.");
        }

        await EnsureConnectedAsync(settings, cancellationToken);
        if (await _obs!.IsReplayBufferActiveAsync(cancellationToken))
        {
            return new CaptureImpactResult(false, false, null, null, null, null, null, null, null, 0, 0,
                "Replay يعمل الآن، لذلك لا توجد baseline نظيفة للمقارنة. أوقف Shadow Capture ثم شغّل الاختبار.");
        }

        var streaming = await SafeAsync(() => _obs.IsStreamActiveAsync(cancellationToken));
        var recording = await SafeAsync(() => _obs.IsRecordActiveAsync(cancellationToken));
        if (streaming || recording)
        {
            return new CaptureImpactResult(false, false, null, null, null, null, null, null, null, 0, 0,
                "D7KT رفض Impact Test أثناء بث/تسجيل فعلي حتى لا يغير Replay Buffer وسط جلسة مهمة. اختبره قبل البث.");
        }

        var baseline = await CollectRuntimeMeasurementsAsync(settings.ImpactTestSeconds, cancellationToken);
        if (baseline.Count < 3 || baseline.Count(x => x.Fps.HasValue) < 3)
        {
            return new CaptureImpactResult(false, false, null, null, null, null, null, null, null, 0, 0,
                "عينات PresentMon غير كافية لعمل baseline موثوقة.");
        }

        var originalEnabled = settings.Enabled;
        try
        {
            await StartAsync(cancellationToken);
            await Task.Delay(1000, cancellationToken);
            var replay = await CollectRuntimeMeasurementsAsync(settings.ImpactTestSeconds, cancellationToken);
            if (replay.Count < 3 || replay.Count(x => x.Fps.HasValue) < 3)
            {
                return new CaptureImpactResult(false, false, null, null, null, null, null, null, null, 0, 0,
                    "بدأ Replay لكن عينات اللعبة بعد التشغيل غير كافية للمقارنة.");
            }

            var stats = await SafeStatsAsync(cancellationToken) ?? new ObsRuntimeStats(0, 0, 0, 0, 0, 0, 0, 0, 0);
            var baseFps = Avg(baseline, x => x.Fps);
            var replayFps = Avg(replay, x => x.Fps);
            var baseLow = Avg(baseline, x => x.OnePercentLow);
            var replayLow = Avg(replay, x => x.OnePercentLow);
            var baseP99 = Avg(baseline, x => x.P99FrameMs);
            var replayP99 = Avg(replay, x => x.P99FrameMs);
            var baseGpu = Avg(baseline, x => x.GpuLoad);
            var replayGpu = Avg(replay, x => x.GpuLoad);
            double? fpsLoss = baseFps.HasValue && replayFps.HasValue ? Math.Max(0, baseFps.Value - replayFps.Value) : null;
            double? p99Increase = baseP99.HasValue && replayP99.HasValue ? Math.Max(0, replayP99.Value - baseP99.Value) : null;
            double? gpuIncrease = baseGpu.HasValue && replayGpu.HasValue ? replayGpu.Value - baseGpu.Value : null;

            var fpsPass = !fpsLoss.HasValue || fpsLoss <= settings.MaxFpsLoss;
            var gpuPass = !gpuIncrease.HasValue || gpuIncrease <= settings.MaxGpuBudgetPercent;
            var obsPass = stats.RenderSkipPercent < 0.5 && stats.OutputSkipPercent < 0.5;
            var p99Pass = !p99Increase.HasValue || p99Increase < 3.0;
            var passed = fpsPass && gpuPass && obsPass && p99Pass;

            var verdict = passed
                ? $"PASS • فرق FPS {Fmt(fpsLoss)} • فرق GPU load {Fmt(gpuIncrease)}% • P99 +{Fmt(p99Increase)}ms • OBS render/output skip {stats.RenderSkipPercent:0.00}%/{stats.OutputSkipPercent:0.00}%."
                : $"FAIL • FPS loss {Fmt(fpsLoss)} (limit {settings.MaxFpsLoss}) • GPU load delta {Fmt(gpuIncrease)}% (budget {settings.MaxGpuBudgetPercent}%) • P99 +{Fmt(p99Increase)}ms • OBS skip {stats.RenderSkipPercent:0.00}%/{stats.OutputSkipPercent:0.00}%. جرّب جودة/bitrate أخف أو اترك Replay متوقف وقت الرانك.";

            return new CaptureImpactResult(true, passed, baseFps, replayFps, fpsLoss, baseLow, replayLow, p99Increase,
                gpuIncrease, stats.RenderSkipPercent, stats.OutputSkipPercent, verdict);
        }
        finally
        {
            try { await StopAsync(cancellationToken); } catch { }
            var restored = LoadSettings();
            restored.Enabled = originalEnabled;
            _settingsStore.Save(restored);
        }
    }

    public async Task<string> ReconfigureAsync(CancellationToken cancellationToken = default)
    {
        var settings = LoadSettings();
        if (!settings.Enabled)
            return "تم حفظ الإعدادات. Shadow Capture غير مفعّل حاليًا.";
        if (_adoptedExistingReplay)
            return "تم حفظ إعدادات D7KT، لكن Replay الحالي ملك OBS وكان شغالًا مسبقًا؛ لن يعيد D7KT ضبطه بالقوة.";
        return await StartAsync(cancellationToken);
    }

    private async Task<string> OrganizeSavedReplayAsync(string saved, ShadowCaptureSettings settings, CancellationToken token)
    {
        var game = SanitizeName(D7RuntimeBus.Context?.PrimaryGame ?? "Desktop");
        var targetFolder = settings.UseGameSubfolders
            ? Path.Combine(settings.SaveFolder, game)
            : settings.SaveFolder;
        Directory.CreateDirectory(targetFolder);

        var ext = Path.GetExtension(saved);
        var fileName = settings.AutoNameWithGame
            ? $"{game}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{settings.ReplaySeconds}s{ext}"
            : Path.GetFileName(saved);
        var destination = UniquePath(Path.Combine(targetFolder, fileName));

        if (!Path.GetFullPath(saved).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            File.Move(saved, destination);
        else
            destination = saved;

        if (settings.CreateMetadataSidecar)
            await WriteMetadataAsync(destination, game, settings, token);

        return destination;
    }

    private async Task WriteMetadataAsync(string path, string game, ShadowCaptureSettings settings, CancellationToken token)
    {
        var hardware = D7RuntimeBus.Hardware;
        var sample = D7RuntimeBus.SessionSample;
        ObsRuntimeStats? stats = null;
        string? encoder = null;
        try
        {
            stats = await SafeStatsAsync(token);
            var mode = await GetOutputModeAsync(token);
            var section = IsAdvanced(mode) ? "AdvOut" : "SimpleOutput";
            encoder = await _obs!.GetProfileParameterAsync(section, "RecEncoder", token);
        }
        catch { }

        var meta = new D7ClipMetadata(
            1,
            DateTimeOffset.Now,
            game,
            D7MissionEngine.MissionArabic(D7RuntimeBus.Mission),
            settings.ReplaySeconds,
            "OBS Replay Buffer",
            encoder,
            new FileInfo(path).Length,
            hardware?.CpuLoad,
            hardware?.GpuLoad,
            hardware?.RamLoad,
            hardware?.VramLoad,
            hardware?.CpuTemp,
            hardware?.GpuTemp,
            sample?.Fps,
            sample?.OnePercentLow,
            sample?.P99FrameMs,
            stats?.RenderSkipPercent,
            stats?.OutputSkipPercent);

        await File.WriteAllTextAsync(path + ".d7.json", JsonSerializer.Serialize(meta, JsonOptions), token);
    }

    private async Task<ShadowObsBackup> CaptureBackupAsync(CancellationToken token)
    {
        var mode = await GetOutputModeAsync(token);
        var section = IsAdvanced(mode) ? "AdvOut" : "SimpleOutput";
        return new ShadowObsBackup(
            DateTimeOffset.Now,
            mode,
            section,
            await _obs!.GetProfileParameterAsync(section, "RecRB", token),
            await _obs.GetProfileParameterAsync(section, "RecRBTime", token),
            await _obs.GetProfileParameterAsync(section, "RecRBSize", token),
            await _obs.GetProfileParameterAsync(section, "RecRBPrefix", token),
            await SafeStringAsync(() => _obs.GetRecordDirectoryAsync(token)));
    }

    private void SaveBackup(ShadowObsBackup backup)
        => File.WriteAllText(_backupPath, JsonSerializer.Serialize(backup, JsonOptions));

    private ShadowObsBackup? LoadBackup()
    {
        try
        {
            if (!File.Exists(_backupPath)) return null;
            return JsonSerializer.Deserialize<ShadowObsBackup>(File.ReadAllText(_backupPath), JsonOptions);
        }
        catch { return null; }
    }

    private async Task RestoreBackupAsync(ShadowObsBackup backup, CancellationToken token)
    {
        await RestoreParameterAsync(backup.Section, "RecRB", backup.RecRb, token);
        await RestoreParameterAsync(backup.Section, "RecRBTime", backup.RecRbTime, token);
        await RestoreParameterAsync(backup.Section, "RecRBSize", backup.RecRbSize, token);
        await RestoreParameterAsync(backup.Section, "RecRBPrefix", backup.RecRbPrefix, token);
        if (!string.IsNullOrWhiteSpace(backup.RecordDirectory))
            await _obs!.SetRecordDirectoryAsync(backup.RecordDirectory, token);
        try { File.Delete(_backupPath); } catch { }
    }

    private async Task RestoreParameterAsync(string section, string name, string? value, CancellationToken token)
    {
        if (value != null)
            await _obs!.SetProfileParameterAsync(section, name, value, token);
    }

    private async Task<string?> GetCurrentReplayPrefixAsync(CancellationToken token)
    {
        try
        {
            var mode = await GetOutputModeAsync(token);
            return await _obs!.GetProfileParameterAsync(IsAdvanced(mode) ? "AdvOut" : "SimpleOutput", "RecRBPrefix", token);
        }
        catch { return null; }
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
            .Where(x => VideoExtensions.Contains(Path.GetExtension(x)))
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
        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(250, cancellationToken);

            if (!Directory.Exists(folder)) continue;
            var candidate = Directory.EnumerateFiles(folder)
                .Where(x => VideoExtensions.Contains(Path.GetExtension(x)))
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

        // Safety invariant: only files with a D7 sidecar are managed. An unrelated MP4 in the
        // same folder is invisible to auto-cleanup and will never be removed by this routine.
        var tracked = Directory.EnumerateFiles(settings.SaveFolder, "*.d7.json", SearchOption.AllDirectories)
            .Select(sidecar => new { Sidecar = sidecar, Video = sidecar[..^".d7.json".Length] })
            .Where(x => File.Exists(x.Video) && VideoExtensions.Contains(Path.GetExtension(x.Video)))
            .Select(x => new { x.Sidecar, File = new FileInfo(x.Video) })
            .OrderByDescending(x => x.File.LastWriteTimeUtc)
            .ToList();

        long total = tracked.Sum(x => x.File.Length);
        foreach (var item in tracked.OrderBy(x => x.File.LastWriteTimeUtc))
        {
            if (total <= maxBytes) break;
            try
            {
                var size = item.File.Length;
                item.File.Delete();
                try { File.Delete(item.Sidecar); } catch { }
                total -= size;
            }
            catch { }
        }
    }

    private static string DetectOtherCaptureApps()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            try { names.Add(process.ProcessName); }
            catch { }
            finally { process.Dispose(); }
        }

        var detected = new List<string>();
        if (names.Any(x => x.Contains("Medal", StringComparison.OrdinalIgnoreCase))) detected.Add("Medal");
        if (names.Any(x => x.Contains("SteelSeriesGG", StringComparison.OrdinalIgnoreCase))) detected.Add("SteelSeries GG/Moments");
        if (names.Any(x => x.Contains("Outplayed", StringComparison.OrdinalIgnoreCase))) detected.Add("Outplayed");
        if (names.Any(x => x.Equals("GameBarFTServer", StringComparison.OrdinalIgnoreCase))) detected.Add("Xbox Game Bar capture service");

        return detected.Count == 0
            ? string.Empty
            : "تطبيقات Capture أخرى مكتشفة: " + string.Join("، ", detected) + ". وجود العملية لا يثبت أنها تسجل الآن؛ D7KT لا يوقفها تلقائيًا.";
    }

    private static (string Level, string Detail) BuildHealth(
        ShadowCaptureSettings settings,
        ObsRuntimeStats? stats,
        GameSessionSample? sample,
        HardwareSnapshot? hardware,
        string duplicates)
    {
        if (stats == null)
            return ("Unknown", "تعذر قراءة OBS Stats؛ لا توجد أرقام أداء كافية للحكم.");

        var problems = new List<string>();
        if (stats.RenderSkipPercent >= 0.5) problems.Add($"render skip {stats.RenderSkipPercent:0.00}%");
        if (stats.OutputSkipPercent >= 0.5) problems.Add($"output skip {stats.OutputSkipPercent:0.00}%");
        if (hardware?.GpuLoad >= 99) problems.Add($"GPU {hardware.GpuLoad:0}%");
        if (sample?.P99FrameMs >= 33.3) problems.Add($"P99 {sample.P99FrameMs:0.0}ms");

        if (problems.Count > 0 && settings.ProtectPerformance)
            return ("Warning", "Performance Guard: " + string.Join(" • ", problems) + ". شغّل Impact Test قبل اعتماد الإعدادات.");
        if (!string.IsNullOrWhiteSpace(duplicates))
            return ("Check", "Replay نفسه سليم، لكن يوجد Capture software آخر يحتاج مراجعة لتجنب ازدواج التسجيل.");
        return ("Good", $"OBS render/output skip {stats.RenderSkipPercent:0.00}%/{stats.OutputSkipPercent:0.00}% • render {stats.AverageFrameRenderTimeMs:0.00}ms.");
    }

    private static async Task<List<RuntimeMeasurement>> CollectRuntimeMeasurementsAsync(int seconds, CancellationToken token)
    {
        var result = new List<RuntimeMeasurement>();
        DateTimeOffset last = DateTimeOffset.MinValue;
        var until = DateTimeOffset.UtcNow.AddSeconds(seconds);
        while (DateTimeOffset.UtcNow < until)
        {
            token.ThrowIfCancellationRequested();
            var s = D7RuntimeBus.SessionSample;
            if (s != null && s.At != last)
            {
                last = s.At;
                result.Add(new RuntimeMeasurement(s.Fps, s.OnePercentLow, s.P99FrameMs, s.GpuLoad));
            }
            await Task.Delay(250, token);
        }
        return result;
    }

    private sealed record RuntimeMeasurement(double? Fps, double? OnePercentLow, double? P99FrameMs, double? GpuLoad);

    private static double? Avg(IReadOnlyCollection<RuntimeMeasurement> data, Func<RuntimeMeasurement, double?> selector)
    {
        var values = data.Select(selector).Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        return values.Length == 0 ? null : values.Average();
    }

    private static string SanitizeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string((value ?? "Unknown").Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "Unknown" : cleaned;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var folder = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; i < 10000; i++)
        {
            var candidate = Path.Combine(folder, $"{name}_{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        return Path.Combine(folder, $"{name}_{Guid.NewGuid():N}{ext}");
    }

    private async Task<ObsRuntimeStats?> SafeStatsAsync(CancellationToken token)
    {
        try { return await _obs!.GetStatsAsync(token); }
        catch { return null; }
    }

    private static async Task<bool> SafeAsync(Func<Task<bool>> action)
    {
        try { return await action(); }
        catch { return false; }
    }

    private static async Task<string?> SafeStringAsync(Func<Task<string?>> action)
    {
        try { return await action(); }
        catch { return null; }
    }

    private static string Fmt(double? value) => value.HasValue ? value.Value.ToString("0.00") : "—";

    private static string FriendlyConnectionError(Exception ex)
    {
        var text = ex.Message;
        if (text.Contains("actively refused", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("تعذر إجراء اتصال", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Unable to connect", StringComparison.OrdinalIgnoreCase))
        {
            return "تعذر الاتصال بـ OBS WebSocket على 127.0.0.1:4455. من OBS افتح Tools → WebSocket Server Settings وفعّل Enable WebSocket server، ثم احفظ كلمة المرور في D7KT.";
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
