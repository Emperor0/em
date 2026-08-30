using LibreHardwareMonitor.Hardware;

namespace D7SystemIntelligence.Core;

public sealed class HardwareEngine : IDisposable
{
    private readonly Computer _computer;
    private readonly object _gate = new();

    public HardwareEngine()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = true
        };
        _computer.Open();
    }

    public HardwareSnapshot Read()
    {
        lock (_gate)
        {
            foreach (var hw in _computer.Hardware) UpdateRecursive(hw);
            string cpu = "Unknown CPU", gpu = "Unknown GPU";
            float cpuLoad = 0, gpuLoad = 0, cpuTemp = 0, gpuTemp = 0, ramLoad = 0;
            float? vramLoad = null;
            var fans = new List<FanSnapshot>();

            foreach (var hw in Flatten(_computer.Hardware))
            {
                if (hw.HardwareType == HardwareType.Cpu) cpu = hw.Name;
                if (hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel) gpu = hw.Name;
                foreach (var s in hw.Sensors)
                {
                    if (hw.HardwareType == HardwareType.Cpu && s.SensorType == SensorType.Load && s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase)) cpuLoad = s.Value ?? cpuLoad;
                    if (hw.HardwareType == HardwareType.Cpu && s.SensorType == SensorType.Temperature && (s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))) cpuTemp = Math.Max(cpuTemp, s.Value ?? 0);
                    if (hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                    {
                        if (s.SensorType == SensorType.Load && (s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase))) gpuLoad = Math.Max(gpuLoad, s.Value ?? 0);
                        if (s.SensorType == SensorType.Temperature && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)) gpuTemp = Math.Max(gpuTemp, s.Value ?? 0);
                        if (s.SensorType == SensorType.Load && s.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase)) vramLoad = Math.Max(vramLoad ?? 0, s.Value ?? 0);
                    }
                    if (hw.HardwareType == HardwareType.Memory && s.SensorType == SensorType.Load) ramLoad = Math.Max(ramLoad, s.Value ?? 0);
                    if (s.SensorType == SensorType.Fan)
                    {
                        var ctl = s.Control;
                        fans.Add(new FanSnapshot(s.Identifier.ToString(), s.Name, s.Value, ctl != null, ctl?.SoftwareValue));
                    }
                }
            }
            return new HardwareSnapshot(cpu, gpu, cpuLoad, gpuLoad, cpuTemp, gpuTemp, ramLoad, vramLoad, fans);
        }
    }

    public bool SetFanControl(string sensorId, float percent, out string message)
    {
        lock (_gate)
        {
            percent = Math.Clamp(percent, 30f, 100f);
            foreach (var hw in Flatten(_computer.Hardware))
            foreach (var s in hw.Sensors)
            {
                if (!string.Equals(s.Identifier.ToString(), sensorId, StringComparison.OrdinalIgnoreCase)) continue;
                if (s.Control == null)
                {
                    message = "Fan is read-only on this controller.";
                    return false;
                }

                try
                {
                    s.Control.SetSoftware(percent);
                    var verified = s.Control.SoftwareValue;
                    if (Math.Abs(verified - percent) <= 2f)
                    {
                        message = $"{s.Name}: {percent:0}% [Applied + Verified].";
                        return true;
                    }

                    try { s.Control.SetDefault(); } catch { }
                    message = $"{s.Name}: write not verified ({verified:0}% read back vs {percent:0}% requested); restored Default.";
                    return false;
                }
                catch (Exception ex)
                {
                    try { s.Control.SetDefault(); } catch { }
                    message = $"{s.Name}: fan write failed; restored Default. {ex.Message}";
                    return false;
                }
            }
            message = "Fan sensor not found.";
            return false;
        }
    }

    public void RestoreFans()
    {
        lock (_gate)
        {
            foreach (var hw in Flatten(_computer.Hardware))
            foreach (var s in hw.Sensors)
                if (s.Control != null) try { s.Control.SetDefault(); } catch { }
        }
    }

    private static IEnumerable<IHardware> Flatten(IEnumerable<IHardware> root)
    {
        foreach (var h in root)
        {
            yield return h;
            foreach (var sub in Flatten(h.SubHardware)) yield return sub;
        }
    }

    private static void UpdateRecursive(IHardware h)
    {
        h.Update();
        foreach (var sub in h.SubHardware) UpdateRecursive(sub);
    }

    public void Dispose()
    {
        RestoreFans();
        _computer.Close();
    }
}
