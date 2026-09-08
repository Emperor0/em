using D7.Hardware.Telemetry;
using Xunit;

namespace D7.UnitTests;

public sealed class D7SelfOverheadGuardTests
{
    [Fact]
    public void ShortSpike_DoesNotThrottle()
    {
        var guard = new D7SelfOverheadGuard(highThresholdPercent: 2, recoveryThresholdPercent: 1, samplesToThrottle: 4, samplesToRecover: 3);

        Assert.False(guard.Observe(0.5).ThrottleTelemetry);
        Assert.False(guard.Observe(3.5).ThrottleTelemetry);
        Assert.False(guard.Observe(0.6).ThrottleTelemetry);
        Assert.False(guard.Observe(3.0).ThrottleTelemetry);
    }

    [Fact]
    public void SustainedHighCpu_ThrottlesOnlyAfterRequiredSamples()
    {
        var guard = new D7SelfOverheadGuard(highThresholdPercent: 2, recoveryThresholdPercent: 1, samplesToThrottle: 3, samplesToRecover: 3);

        Assert.False(guard.Observe(2.5).ThrottleTelemetry);
        Assert.False(guard.Observe(2.7).ThrottleTelemetry);
        var decision = guard.Observe(2.2);

        Assert.True(decision.ThrottleTelemetry);
        Assert.True(decision.StateChanged);
    }

    [Fact]
    public void ThrottledState_RequiresSustainedRecoveryBeforeNormalPolling()
    {
        var guard = new D7SelfOverheadGuard(highThresholdPercent: 2, recoveryThresholdPercent: 1, samplesToThrottle: 2, samplesToRecover: 3);
        _ = guard.Observe(3);
        Assert.True(guard.Observe(3).ThrottleTelemetry);

        Assert.True(guard.Observe(0.8).ThrottleTelemetry);
        Assert.True(guard.Observe(0.7).ThrottleTelemetry);
        var recovered = guard.Observe(0.6);

        Assert.False(recovered.ThrottleTelemetry);
        Assert.True(recovered.StateChanged);
    }

    [Fact]
    public void MissingSample_DoesNotChangeState()
    {
        var guard = new D7SelfOverheadGuard(highThresholdPercent: 2, recoveryThresholdPercent: 1, samplesToThrottle: 2, samplesToRecover: 2);

        var decision = guard.Observe(null);

        Assert.False(decision.ThrottleTelemetry);
        Assert.False(decision.StateChanged);
    }
}
