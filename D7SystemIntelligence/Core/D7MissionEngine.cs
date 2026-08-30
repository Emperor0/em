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

public enum MissionStepState
{
    Applied,
    Verified,
    AlreadyOptimal,
    Unsupported,
    Skipped,
    Failed,
    Restored
}

public sealed record MissionStepResult(
    string Step,
    bool Success,
    string Detail,
    MissionStepState State = MissionStepState.Applied);

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

        if (ActiveMission == mission)
        {
            var step = new MissionStepResult(
                "Mission State",
                true,
                $"Already Active • {MissionArabic(mission)} تعمل بالفعل. D7KT لم يفك ويعيد تطبيق نفس السياسات بلا سبب.",
                MissionStepState.AlreadyOptimal);
            return new MissionApplyResult(mission, true, [step], $"{MissionArabic(mission)} ما زالت نشطة بدون إعادة تطبيق غير ضرورية.");
        }

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
                    steps.Add(new MissionStepResult("المراوح", true, "Restore Verified • تم إرجاع Fan override إلى BIOS/AUTO.", MissionStepState.Restored));
                }
                else
                {
                    steps.Add(new MissionStepResult("المراوح", true, "Already Optimal • لا يوجد Fan override من D7KT يحتاج إيقافه.", MissionStepState.AlreadyOptimal));
                }
                break;
        }

        var success = steps.All(x => x.State != MissionStepState.Failed);
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
            try { steps.Add(new MissionStepResult("Shadow Capture", true, await _shadow.StopAsync(cancellationToken), MissionStepState.Restored)); }
            catch (Exception ex) { steps.Add(Failed("Shadow Capture", ex.Message)); }
            _shadowStartedByMission = false;
        }

        if (_streamGovernorApplied)
        {
            steps.Add(new MissionStepResult("Stream Governor", true, _streamGovernor.Restore(), MissionStepState.Restored));
            _streamGovernorApplied = false;
        }

        if (_fansStarted)
        {
            _fans.Stop(true);
            steps.Add(new MissionStepResult("المراوح", true, "Restore Verified • تم إيقاف AUTO Fan واستعادة BIOS/Default.", MissionStepState.Restored));
            _fansStarted = false;
        }

        if (_networkApplied)
        {
            try
            {
                var r = await _network.RestoreAsync(cancellationToken);
                steps.Add(new MissionStepResult("الشبكة", r.Success, r.Detail, r.Success ? MissionStepState.Restored : MissionStepState.Failed));
            }
            catch (Exception ex) { steps.Add(Failed("الشبكة", ex.Message)); }
            _networkApplied = false;
        }

        if (_displayApplied)
        {
            try
            {
                var detail = _display.Restore();
                var ok = !detail.StartsWith("فشل", StringComparison.Ordinal) && !detail.StartsWith("تعذر", StringComparison.Ordinal);
                steps.Add(new MissionStepResult("الشاشة", ok, detail, ok ? MissionStepState.Restored : MissionStepState.Failed));
            }
            catch (Exception ex) { steps.Add(Failed("الشاشة", ex.Message)); }
            _displayApplied = false;
        }

        if (_powerApplied)
        {
            try
            {
                var r = await _power.RestoreAsync(cancellationToken);
                steps.Add(new MissionStepResult("الطاقة", r.Success, r.Detail, r.Success ? MissionStepState.Restored : MissionStepState.Failed));
            }
            catch (Exception ex) { steps.Add(Failed("الطاقة", ex.Message)); }
            _powerApplied = false;
        }

        ActiveMission = D7Mission.None;
        D7RuntimeBus.PublishMission(D7Mission.None);
        var ok = steps.All(x => x.State != MissionStepState.Failed);
        var summary = previous == D7Mission.None
            ? "لا توجد Mission نشطة ولا تغييرات مملوكة لـD7KT تحتاج استعادة."
            : ok ? $"تم إنهاء {MissionArabic(previous)} واستعادة كل تغيير كان D7KT يملكه."
            : $"انتهت {MissionArabic(previous)} لكن فشل التحقق من استعادة خطوة أو أكثر. راجع التفاصيل.";
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
                _networkApplied = r.Success && r.ChangedProperties > 0;
                var state = !r.Success ? MissionStepState.Failed
                    : r.ChangedProperties > 0 ? MissionStepState.Applied
                    : MissionStepState.Unsupported;
                steps.Add(new MissionStepResult("الشبكة", r.Success, r.Detail, state));
            }
            catch (Exception ex) { steps.Add(Failed("الشبكة", ex.Message)); }
        }

        if (cleanBackground)
        {
            try
            {
                var detail = await _background.SmartCleanAsync(token);
                var changed = detail.StartsWith("تم إغلاق", StringComparison.Ordinal);
                steps.Add(new MissionStepResult(
                    "الخلفية",
                    true,
                    detail,
                    changed ? MissionStepState.Applied : MissionStepState.AlreadyOptimal));
            }
            catch (Exception ex) { steps.Add(Failed("الخلفية", ex.Message)); }
        }
    }

    private async Task ApplyPowerAsync(List<MissionStepResult> steps, CancellationToken token)
    {
        try
        {
            var r = await _power.ApplyHighPerformanceAsync(token);
            _powerApplied = r.Success && r.Changed;
            steps.Add(new MissionStepResult(
                "الطاقة",
                r.Success,
                r.Detail,
                !r.Success ? MissionStepState.Failed : r.Changed ? MissionStepState.Verified : MissionStepState.AlreadyOptimal));
        }
        catch (Exception ex) { steps.Add(Failed("الطاقة", ex.Message)); }
    }

    private async Task ApplyBalancedPowerAsync(List<MissionStepResult> steps, CancellationToken token)
    {
        try
        {
            var r = await _power.ApplyBalancedAsync(token);
            _powerApplied = r.Success && r.Changed;
            steps.Add(new MissionStepResult(
                "الطاقة",
                r.Success,
                r.Detail,
                !r.Success ? MissionStepState.Failed : r.Changed ? MissionStepState.Verified : MissionStepState.AlreadyOptimal));
        }
        catch (Exception ex) { steps.Add(Failed("الطاقة", ex.Message)); }
    }

    private void ApplyDisplayMax(List<MissionStepResult> steps)
    {
        try
        {
            var before = _display.GetCurrentMode();
            if (before == null)
            {
                steps.Add(new MissionStepResult("الشاشة", true, "Unsupported • تعذر قراءة وضع الشاشة الحالي؛ لم يرسل D7KT أي تغيير.", MissionStepState.Unsupported));
                return;
            }

            var max = _display.GetModesForCurrentResolution().OrderByDescending(x => x.RefreshRateHz).FirstOrDefault();
            if (max == null)
            {
                steps.Add(new MissionStepResult("الشاشة", true, "Unsupported • Windows لم يعرض أوضاع Refresh للدقة الحالية.", MissionStepState.Unsupported));
                return;
            }

            if (before.RefreshRateHz >= max.RefreshRateHz)
            {
                steps.Add(new MissionStepResult("الشاشة", true, $"Already Optimal • {before.RefreshRateHz}Hz هي أعلى Refresh متاحة للدقة الحالية.", MissionStepState.AlreadyOptimal));
                return;
            }

            var detail = _display.ApplyRefreshRate(max.RefreshRateHz);
            var after = _display.GetCurrentMode();
            var verified = after?.RefreshRateHz == max.RefreshRateHz;
            _displayApplied = verified && before.RefreshRateHz != after!.RefreshRateHz;
            steps.Add(new MissionStepResult(
                "الشاشة",
                verified,
                verified ? $"Applied + Verified • {before.RefreshRateHz}Hz → {after!.RefreshRateHz}Hz." : detail,
                verified ? MissionStepState.Verified : MissionStepState.Failed));
        }
        catch (Exception ex) { steps.Add(Failed("الشاشة", ex.Message)); }
    }

    private void StartSmartFans(List<MissionStepResult> steps)
    {
        try
        {
            var count = _hardware.Read().Fans.Count(x => x.Controllable);
            if (count == 0)
            {
                steps.Add(new MissionStepResult(
                    "المراوح",
                    true,
                    "Unsupported / Read-only • الهاردوير لم يعرض قناة Fan writable آمنة؛ D7KT لم يرسل PWM عشوائي.",
                    MissionStepState.Unsupported));
                return;
            }

            _fansStarted = _fans.Start();
            steps.Add(new MissionStepResult(
                "المراوح",
                _fansStarted,
                _fansStarted ? $"Applied • AUTO Fan يعمل على {count} قناة writable." : "فشل بدء AUTO Fan رغم وجود قناة writable.",
                _fansStarted ? MissionStepState.Applied : MissionStepState.Failed));
        }
        catch (Exception ex) { steps.Add(Failed("المراوح", ex.Message)); }
    }

    private void ApplyStreamGovernor(List<MissionStepResult> steps, string? gameProcessName)
    {
        try
        {
            var detail = _streamGovernor.Apply(gameProcessName);
            _streamGovernorApplied = _streamGovernor.Active;
            var state = _streamGovernor.ChangedCount > 0 ? MissionStepState.Verified
                : detail.StartsWith("Already Optimal", StringComparison.Ordinal) ? MissionStepState.AlreadyOptimal
                : MissionStepState.Unsupported;
            steps.Add(new MissionStepResult("Stream Governor", true, detail, state));
        }
        catch (Exception ex) { steps.Add(Failed("Stream Governor", ex.Message)); }
    }

    private async Task StartConfiguredReplayIfEnabledAsync(List<MissionStepResult> steps, CancellationToken token)
    {
        if (!_shadow.LoadSettings().Enabled)
        {
            steps.Add(new MissionStepResult(
                "Shadow Capture",
                true,
                "Skipped by user policy • Shadow Capture غير مفعّل في إعداداتك؛ STREAM + RANKED لن يشغله من نفسه.",
                MissionStepState.Skipped));
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
                steps.Add(new MissionStepResult(
                    "Shadow Capture",
                    true,
                    "Already Optimal • Replay Buffer شغال مسبقًا؛ D7KT لم يشغل Recorder أو Replay ثاني.",
                    MissionStepState.AlreadyOptimal));
                return;
            }

            var detail = await _shadow.StartAsync(token);
            var verify = await _shadow.GetStatusAsync(token);
            _shadowStartedByMission = verify.ReplayActive;
            steps.Add(new MissionStepResult(
                "Shadow Capture",
                verify.ReplayActive,
                verify.ReplayActive ? "Applied + Verified • " + detail : "تم طلب Replay لكن التحقق لم يثبت أنه شغال.",
                verify.ReplayActive ? MissionStepState.Verified : MissionStepState.Failed));
        }
        catch (Exception ex) { steps.Add(Failed("Shadow Capture", ex.Message)); }
    }

    private static MissionStepResult Failed(string step, string detail)
        => new(step, false, detail, MissionStepState.Failed);

    private static string BuildSummary(D7Mission mission, IReadOnlyCollection<MissionStepResult> steps, bool success)
    {
        var applied = steps.Count(x => x.State is MissionStepState.Applied or MissionStepState.Verified);
        var optimal = steps.Count(x => x.State == MissionStepState.AlreadyOptimal);
        var unsupported = steps.Count(x => x.State == MissionStepState.Unsupported);
        var skipped = steps.Count(x => x.State == MissionStepState.Skipped);
        var failed = steps.Count(x => x.State == MissionStepState.Failed);

        var result = success ? "PASS" : "PARTIAL/FAIL";
        return $"{MissionArabic(mission)} • {result} • changed/verified {applied} • already optimal {optimal} • unsupported {unsupported} • skipped {skipped} • failed {failed}.";
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
