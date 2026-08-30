namespace D7SystemIntelligence.Core;

public enum D7Mission
{
    None,
    ProRanked,
    StreamRanked,
    Recording,
    Story,
    Silent
}

public sealed record MissionStepResult(string Step, bool Success, string Detail);
public sealed record MissionApplyResult(D7Mission Mission, bool Success, IReadOnlyList<MissionStepResult> Steps, string Summary);

public sealed class D7MissionEngine : IAsyncDisposable
{
    private readonly HardwareEngine _hardware;
    private readonly NetworkGamingProfileService _network = new();
    private readonly PowerPlanService _power = new();
    private readonly DisplayControlService _display = new();
    private readonly BackgroundAppManagerService _background = new();
    private readonly ShadowCaptureService _shadow = new();
    private readonly StreamProcessGovernor _streamGovernor = new();
    private readonly SmartFanController _fans;

    private bool _networkApplied;
    private bool _powerApplied;
    private bool _displayApplied;
    private bool _fansStarted;
    private bool _shadowStartedByMission;
    private bool _streamGovernorApplied;

    public D7Mission ActiveMission { get; private set; }
    public event Action<string>? StatusChanged;

    public D7MissionEngine(HardwareEngine hardware)
    {
        _hardware = hardware;
        _fans = new SmartFanController(hardware);
        _fans.StatusChanged += s => StatusChanged?.Invoke(s);
        D7RuntimeBus.PublishMission(D7Mission.None);
    }

    public async Task<MissionApplyResult> ApplyAsync(D7Mission mission, string? gameProcessName, CancellationToken cancellationToken = default)
    {
        if (mission == D7Mission.None)
            return await RestoreAsync(cancellationToken);

        if (ActiveMission != D7Mission.None)
            await RestoreAsync(cancellationToken);

        var steps = new List<MissionStepResult>();
        ActiveMission = mission;
        D7RuntimeBus.PublishMission(mission);
        StatusChanged?.Invoke($"جاري تطبيق مهمة {MissionArabic(mission)}…");

        switch (mission)
        {
            case D7Mission.ProRanked:
                await ApplyPerformanceFoundationAsync(steps, cancellationToken, includeNetwork: true, cleanBackground: true);
                break;
            case D7Mission.StreamRanked:
                await ApplyPerformanceFoundationAsync(steps, cancellationToken, includeNetwork: true, cleanBackground: false);
                ApplyStreamGovernor(steps, gameProcessName);
                await StartConfiguredReplayIfEnabledAsync(steps, cancellationToken);
                break;
            case D7Mission.Recording:
                await ApplyPerformanceFoundationAsync(steps, cancellationToken, includeNetwork: false, cleanBackground: false);
                await StartReplayAsync(steps, cancellationToken);
                break;
            case D7Mission.Story:
                await ApplyPowerAsync(steps, cancellationToken);
                ApplyDisplayMax(steps);
                StartSmartFans(steps);
                break;
            case D7Mission.Silent:
                await ApplyBalancedPowerAsync(steps, cancellationToken);
                if (_fans.IsRunning)
                {
                    _fans.Stop(true);
                    _fansStarted = false;
                    steps.Add(new MissionStepResult("المراوح", true, "تمت إعادة المراوح إلى BIOS/AUTO بدل فرض Curve أثناء Silent."));
                }
                break;
        }

        var success = steps.All(x => x.Success || IsOptionalStep(x.Step));
        var summary = BuildSummary(mission, steps, success);
        StatusChanged?.Invoke(summary);
        return new MissionApplyResult(mission, success, steps, summary);
    }

    public async Task<MissionApplyResult> RestoreAsync(CancellationToken cancellationToken = default)
    {
        var previous = ActiveMission;
        var steps = new List<MissionStepResult>();

        if (_shadowStartedByMission)
        {
            try { steps.Add(new MissionStepResult("Shadow Capture", true, await _shadow.StopAsync(cancellationToken))); }
            catch (Exception ex) { steps.Add(new MissionStepResult("Shadow Capture", false, ex.Message)); }
            _shadowStartedByMission = false;
        }

        if (_streamGovernorApplied)
        {
            steps.Add(new MissionStepResult("Stream Governor", true, _streamGovernor.Restore()));
            _streamGovernorApplied = false;
        }

        if (_fansStarted)
        {
            _fans.Stop(true);
            steps.Add(new MissionStepResult("المراوح", true, "تم إيقاف AUTO Fan واستعادة BIOS/Default."));
            _fansStarted = false;
        }

        if (_networkApplied)
        {
            try
            {
                var r = await _network.RestoreAsync(cancellationToken);
                steps.Add(new MissionStepResult("الشبكة", r.Success, r.Detail));
            }
            catch (Exception ex) { steps.Add(new MissionStepResult("الشبكة", false, ex.Message)); }
            _networkApplied = false;
        }

        if (_displayApplied)
        {
            try
            {
                var detail = _display.Restore();
                steps.Add(new MissionStepResult("الشاشة", !detail.StartsWith("فشل", StringComparison.Ordinal), detail));
            }
            catch (Exception ex) { steps.Add(new MissionStepResult("الشاشة", false, ex.Message)); }
            _displayApplied = false;
        }

        if (_powerApplied)
        {
            try
            {
                var r = await _power.RestoreAsync(cancellationToken);
                steps.Add(new MissionStepResult("الطاقة", r.Success, r.Detail));
            }
            catch (Exception ex) { steps.Add(new MissionStepResult("الطاقة", false, ex.Message)); }
            _powerApplied = false;
        }

        ActiveMission = D7Mission.None;
        D7RuntimeBus.PublishMission(D7Mission.None);
        var ok = steps.All(x => x.Success || IsOptionalStep(x.Step));
        var summary = previous == D7Mission.None
            ? "لا توجد Mission نشطة تحتاج استعادة."
            : ok ? $"تم إنهاء {MissionArabic(previous)} واستعادة الإعدادات المحفوظة." : $"انتهت {MissionArabic(previous)} مع تنبيه في بعض خطوات الاستعادة.";
        StatusChanged?.Invoke(summary);
        return new MissionApplyResult(D7Mission.None, ok, steps, summary);
    }

    private async Task ApplyPerformanceFoundationAsync(List<MissionStepResult> steps, CancellationToken token, bool includeNetwork, bool cleanBackground)
    {
        await ApplyPowerAsync(steps, token);
        ApplyDisplayMax(steps);
        StartSmartFans(steps);
        if (includeNetwork)
        {
            try
            {
                var r = await _network.ApplyAsync(token);
                _networkApplied = r.Success;
                steps.Add(new MissionStepResult("الشبكة", r.Success, r.Detail));
            }
            catch (Exception ex) { steps.Add(new MissionStepResult("الشبكة", false, ex.Message)); }
        }
        if (cleanBackground)
        {
            try { steps.Add(new MissionStepResult("الخلفية", true, await _background.SmartCleanAsync(token))); }
            catch (Exception ex) { steps.Add(new MissionStepResult("الخلفية", false, ex.Message)); }
        }
    }

    private async Task ApplyPowerAsync(List<MissionStepResult> steps, CancellationToken token)
    {
        try
        {
            var r = await _power.ApplyHighPerformanceAsync(token);
            _powerApplied = r.Success;
            steps.Add(new MissionStepResult("الطاقة", r.Success, r.Detail));
        }
        catch (Exception ex) { steps.Add(new MissionStepResult("الطاقة", false, ex.Message)); }
    }

    private async Task ApplyBalancedPowerAsync(List<MissionStepResult> steps, CancellationToken token)
    {
        try
        {
            var r = await _power.ApplyBalancedAsync(token);
            _powerApplied = r.Success;
            steps.Add(new MissionStepResult("الطاقة", r.Success, r.Detail));
        }
        catch (Exception ex) { steps.Add(new MissionStepResult("الطاقة", false, ex.Message)); }
    }

    private void ApplyDisplayMax(List<MissionStepResult> steps)
    {
        try
        {
            var before = _display.GetCurrentMode();
            var detail = _display.ApplyMaximumRefresh();
            var after = _display.GetCurrentMode();
            _displayApplied = before != null && after != null && before.RefreshRateHz != after.RefreshRateHz;
            var ok = !detail.StartsWith("فشل", StringComparison.Ordinal) && !detail.StartsWith("تعذر", StringComparison.Ordinal);
            steps.Add(new MissionStepResult("الشاشة", ok, detail));
        }
        catch (Exception ex) { steps.Add(new MissionStepResult("الشاشة", false, ex.Message)); }
    }

    private void StartSmartFans(List<MissionStepResult> steps)
    {
        try
        {
            var count = _hardware.Read().Fans.Count(x => x.Controllable);
            if (count == 0)
            {
                steps.Add(new MissionStepResult("المراوح", true, "قراءة فقط: الهاردوير لم يعرض قناة Fan writable آمنة؛ D7 لم يرسل أي PWM عشوائي."));
                return;
            }
            _fansStarted = _fans.Start();
            steps.Add(new MissionStepResult("المراوح", _fansStarted, _fansStarted ? $"AUTO Fan يعمل على {count} قناة مدعومة." : "تعذر بدء AUTO Fan."));
        }
        catch (Exception ex) { steps.Add(new MissionStepResult("المراوح", false, ex.Message)); }
    }

    private void ApplyStreamGovernor(List<MissionStepResult> steps, string? gameProcessName)
    {
        try
        {
            var detail = _streamGovernor.Apply(gameProcessName);
            _streamGovernorApplied = _streamGovernor.Active;
            steps.Add(new MissionStepResult("Stream Governor", true, detail));
        }
        catch (Exception ex) { steps.Add(new MissionStepResult("Stream Governor", false, ex.Message)); }
    }

    private async Task StartConfiguredReplayIfEnabledAsync(List<MissionStepResult> steps, CancellationToken token)
    {
        if (!_shadow.LoadSettings().Enabled)
        {
            steps.Add(new MissionStepResult("Shadow Capture", true, "Shadow Capture غير مفعّل في إعداداتك؛ Stream Mission لن تفعله من نفسها."));
            return;
        }
        await StartReplayAsync(steps, token);
    }

    private async Task StartReplayAsync(List<MissionStepResult> steps, CancellationToken token)
    {
        try
        {
            var status = await _shadow.GetStatusAsync(token);
            if (status.ReplayActive)
            {
                steps.Add(new MissionStepResult("Shadow Capture", true, "Replay Buffer شغال مسبقًا؛ D7 لم يشغل مسجلًا ثانيًا."));
                return;
            }
            var detail = await _shadow.StartAsync(token);
            _shadowStartedByMission = true;
            steps.Add(new MissionStepResult("Shadow Capture", true, detail));
        }
        catch (Exception ex) { steps.Add(new MissionStepResult("Shadow Capture", false, ex.Message)); }
    }

    private static bool IsOptionalStep(string step) => step is "المراوح" or "Shadow Capture" or "الشاشة";

    private static string BuildSummary(D7Mission mission, IReadOnlyCollection<MissionStepResult> steps, bool success)
    {
        var done = steps.Count(x => x.Success);
        var failed = steps.Count - done;
        return success
            ? $"{MissionArabic(mission)} نشطة • {done} خطوة تم تنفيذها/التحقق منها."
            : $"{MissionArabic(mission)} اشتغلت جزئيًا • نجاح {done} • تنبيه {failed}. راجع التفاصيل.";
    }

    public static string MissionArabic(D7Mission mission) => mission switch
    {
        D7Mission.ProRanked => "PRO RANKED",
        D7Mission.StreamRanked => "STREAM + RANKED",
        D7Mission.Recording => "RECORDING",
        D7Mission.Story => "STORY / ULTRA",
        D7Mission.Silent => "SILENT",
        _ => "الوضع الطبيعي"
    };

    public async ValueTask DisposeAsync()
    {
        try { await RestoreAsync(); } catch { }
        _fans.Dispose();
        _streamGovernor.Dispose();
        await _shadow.DisposeAsync();
    }
}
