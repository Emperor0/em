using D7.Hardware.Discovery;
using D7.Hardware.Models;
using D7.Hardware.Telemetry;
using Xunit;

namespace D7.UnitTests;

public sealed class HardwareDiscoveryTests
{
    [Theory]
    [InlineData(null, TriState.Unknown)]
    [InlineData(0, TriState.Disabled)]
    [InlineData(1, TriState.Enabled)]
    [InlineData(2, TriState.Enabled)]
    public void BooleanRegistryDecoder_IsConservative(object? value, TriState expected)
    {
        Assert.Equal(expected, WindowsHardwareDiscoveryService.DecodeBooleanRegistryValue(value));
    }

    [Theory]
    [InlineData(null, TriState.Unknown)]
    [InlineData(0, TriState.Unknown)]
    [InlineData(1, TriState.Disabled)]
    [InlineData(2, TriState.Enabled)]
    [InlineData(3, TriState.Unknown)]
    public void HagsDecoder_OnlyAcceptsKnownWindowsValues(object? value, TriState expected)
    {
        Assert.Equal(expected, WindowsHardwareDiscoveryService.DecodeHagsRegistryValue(value));
    }

    [Fact]
    public void MemoryInfo_ComputesHumanReadableValues()
    {
        var memory = new MemoryInfo(16UL * 1024 * 1024 * 1024, 4UL * 1024 * 1024 * 1024);
        Assert.Equal(16, memory.TotalGiB);
        Assert.Equal(4, memory.AvailableGiB);
        Assert.Equal(75, memory.UsedPercent);
    }

    [Fact]
    public async Task Discovery_ReturnsCoreWindowsInventory_WithoutThrowing()
    {
        if (!OperatingSystem.IsWindows()) return;

        var service = new WindowsHardwareDiscoveryService();
        var snapshot = await service.CaptureAsync(CancellationToken.None);

        Assert.NotNull(snapshot.Cpu);
        Assert.True(snapshot.Cpu.LogicalProcessors > 0);
        Assert.True(snapshot.Memory.TotalBytes > 0);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Windows.Build));
        Assert.NotNull(snapshot.Storage);
        Assert.NotNull(snapshot.NetworkAdapters);
    }

    [Fact]
    public async Task TelemetrySampler_ProducesBoundedCpuAndMemoryValues()
    {
        if (!OperatingSystem.IsWindows()) return;

        var sampler = new SystemTelemetrySampler();
        _ = await sampler.SampleAsync(CancellationToken.None);
        await Task.Delay(50);
        var sample = await sampler.SampleAsync(CancellationToken.None);

        if (sample.CpuUtilizationPercent is not null)
            Assert.InRange(sample.CpuUtilizationPercent.Value, 0, 100);
        Assert.InRange(sample.MemoryUsedPercent, 0, 100);
        Assert.True(sample.D7WorkingSetBytes > 0);
        Assert.True(sample.D7ThreadCount > 0);
    }
}
