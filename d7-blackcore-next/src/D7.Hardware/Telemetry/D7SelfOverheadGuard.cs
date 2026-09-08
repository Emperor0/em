namespace D7.Hardware.Telemetry;

public sealed record D7SelfOverheadDecision(
    bool ThrottleTelemetry,
    bool StateChanged,
    double? LastCpuPercent,
    int HighSamples,
    int RecoverySamples,
    string MessageAr);

public sealed class D7SelfOverheadGuard
{
    private readonly double _highThresholdPercent;
    private readonly double _recoveryThresholdPercent;
    private readonly int _samplesToThrottle;
    private readonly int _samplesToRecover;
    private int _highSamples;
    private int _recoverySamples;
    private bool _throttled;

    public D7SelfOverheadGuard(
        double highThresholdPercent = 2.0,
        double recoveryThresholdPercent = 1.0,
        int samplesToThrottle = 4,
        int samplesToRecover = 6)
    {
        if (highThresholdPercent <= 0) throw new ArgumentOutOfRangeException(nameof(highThresholdPercent));
        if (recoveryThresholdPercent < 0 || recoveryThresholdPercent >= highThresholdPercent)
            throw new ArgumentOutOfRangeException(nameof(recoveryThresholdPercent));
        if (samplesToThrottle < 2) throw new ArgumentOutOfRangeException(nameof(samplesToThrottle));
        if (samplesToRecover < 2) throw new ArgumentOutOfRangeException(nameof(samplesToRecover));

        _highThresholdPercent = highThresholdPercent;
        _recoveryThresholdPercent = recoveryThresholdPercent;
        _samplesToThrottle = samplesToThrottle;
        _samplesToRecover = samplesToRecover;
    }

    public D7SelfOverheadDecision Observe(double? d7CpuPercent)
    {
        var previous = _throttled;

        if (d7CpuPercent is null)
        {
            return Decision(false, d7CpuPercent, "بانتظار عينة كافية لقياس استهلاك D7 نفسه.");
        }

        if (!_throttled)
        {
            _recoverySamples = 0;
            if (d7CpuPercent >= _highThresholdPercent)
            {
                _highSamples++;
                if (_highSamples >= _samplesToThrottle)
                {
                    _throttled = true;
                    _highSamples = 0;
                }
            }
            else
            {
                _highSamples = 0;
            }
        }
        else
        {
            _highSamples = 0;
            if (d7CpuPercent <= _recoveryThresholdPercent)
            {
                _recoverySamples++;
                if (_recoverySamples >= _samplesToRecover)
                {
                    _throttled = false;
                    _recoverySamples = 0;
                }
            }
            else
            {
                _recoverySamples = 0;
            }
        }

        var changed = previous != _throttled;
        var message = _throttled
            ? "استهلاك D7 نفسه مرتفع بشكل مستمر؛ تم تخفيف القياسات الخلفية لحماية ثبات الإطارات."
            : changed
                ? "عاد استهلاك D7 لمستواه المنخفض؛ تم استئناف معدل القياس الطبيعي."
                : "استهلاك D7 ضمن ميزانية الأداء.";

        return new D7SelfOverheadDecision(
            _throttled,
            changed,
            d7CpuPercent,
            _highSamples,
            _recoverySamples,
            message);
    }

    private D7SelfOverheadDecision Decision(bool changed, double? last, string message) =>
        new(_throttled, changed, last, _highSamples, _recoverySamples, message);
}
