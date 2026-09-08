using D7.Core.Models;
using D7.Hardware.Capabilities;
using D7.Hardware.Models;
using Xunit;

namespace D7.UnitTests;

public sealed class HardwareCapabilityTests
{
    [Fact]
    public void DangerousHardwareActions_AreNeverSupportedByDefault()
    {
        var snapshot = SampleSnapshot("NVIDIA GeForce RTX 2060 SUPER");
        var capabilities = new HardwareCapabilityEvaluator().Evaluate(snapshot);

        Assert.Equal(CapabilityState.Experimental, Find(capabilities, "cpu.low_level_tuning").State);
        Assert.Equal(CapabilityState.Experimental, Find(capabilities, "gpu.overclock").State);
        Assert.Equal(CapabilityState.Unavailable, Find(capabilities, "anti_cheat_manipulation").State);
        Assert.Equal(CapabilityState.ReadOnly, Find(capabilities, "ram.analysis").State);
    }

    [Fact]
    public void NvidiaTelemetry_IsReadOnly_WhenNvidiaAdapterExists()
    {
        var capabilities = new HardwareCapabilityEvaluator().Evaluate(SampleSnapshot("NVIDIA GeForce RTX 2060 SUPER"));
        Assert.Equal(CapabilityState.ReadOnly, Find(capabilities, "telemetry.nvidia").State);
    }

    [Fact]
    public void NvidiaTelemetry_IsUnavailable_WhenNoNvidiaAdapterExists()
    {
        var capabilities = new HardwareCapabilityEvaluator().Evaluate(SampleSnapshot("Microsoft Basic Display Adapter"));
        Assert.Equal(CapabilityState.Unavailable, Find(capabilities, "telemetry.nvidia").State);
    }

    private static Capability Find(IReadOnlyList<Capability> capabilities, string id) =>
        Assert.Single(capabilities.Where(x => x.Id == id));

    private static HardwareSnapshot SampleSnapshot(string gpuName) =>
        new(
            DateTimeOffset.UtcNow,
            new CpuInfo("AMD Ryzen 5 3600", 12, "X64"),
            new MemoryInfo(16UL * 1024 * 1024 * 1024, 8UL * 1024 * 1024 * 1024),
            new BoardInfo("Gigabyte", "B450 AORUS ELITE", "F68a", ""),
            new WindowsInfo("Windows 10 Pro", "22H2", "19045", 0, TriState.Unknown, TriState.Enabled, TriState.Disabled, TriState.Disabled, TriState.Disabled, TriState.Enabled, Array.Empty<string>()),
            [new DisplayAdapterInfo(gpuName, "DISPLAY1", "PCI", true, true, false)],
            Array.Empty<StorageInfo>(),
            Array.Empty<NetworkAdapterInfo>(),
            Array.Empty<StartupEntry>());
}
