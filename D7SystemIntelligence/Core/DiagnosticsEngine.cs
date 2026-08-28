using System.Diagnostics;
using Microsoft.Win32;

namespace D7SystemIntelligence.Core;

public sealed class DiagnosticsEngine
{
    public async Task<List<DiagnosticFinding>> RunAsync(HardwareSnapshot hw)
    {
        return await Task.Run(() =>
        {
            var f = new List<DiagnosticFinding>();
            if (hw.CpuTemp >= 90) f.Add(new("Critical","Thermal","CPU temperature is too high",$"CPU reached {hw.CpuTemp:0} °C.","Check cooler mounting, paste, airflow and fan curve."));
            else if (hw.CpuTemp >= 82) f.Add(new("Warning","Thermal","CPU running hot",$"CPU is {hw.CpuTemp:0} °C.","Inspect fan curve and sustained load."));
            if (hw.GpuTemp >= 86) f.Add(new("Warning","Thermal","GPU running hot",$"GPU is {hw.GpuTemp:0} °C.","Inspect GPU fan curve and case airflow."));
            if (hw.RamLoad >= 88) f.Add(new("Warning","Memory","RAM pressure detected",$"Memory usage is {hw.RamLoad:0}%.","Close nonessential apps or consider more RAM."));
            if ((hw.VramLoad ?? 0) >= 92) f.Add(new("Warning","VRAM","VRAM pressure detected",$"VRAM load is {hw.VramLoad:0}%.","Reduce texture/streaming budget before lowering everything else."));
            try
            {
                using var log = new EventLog("System");
                var recent = log.Entries.Cast<EventLogEntry>().Reverse().Take(1200).Where(e => e.TimeGenerated > DateTime.Now.AddDays(-3)).ToArray();
                var whea = recent.Count(e => e.Source.Contains("WHEA", StringComparison.OrdinalIgnoreCase));
                var disk = recent.Count(e => e.Source.Contains("disk", StringComparison.OrdinalIgnoreCase) && e.EntryType == EventLogEntryType.Error);
                var nv = recent.Count(e => e.Source.Contains("nvlddmkm", StringComparison.OrdinalIgnoreCase));
                if (whea > 0) f.Add(new("Critical","Stability","WHEA hardware errors found",$"{whea} recent WHEA entries in System log.","Return CPU/RAM overclock to last known stable profile before further tuning."));
                if (nv > 0) f.Add(new("Warning","GPU Driver","NVIDIA driver errors found",$"{nv} recent nvlddmkm entries.","Check GPU overclock and driver stability."));
                if (disk > 0) f.Add(new("Warning","Storage","Disk errors found",$"{disk} recent disk errors.","Check SMART data, cable/connection and filesystem health."));
            } catch { }
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                var freePct = drive.TotalSize == 0 ? 100 : drive.TotalFreeSpace * 100.0 / drive.TotalSize;
                if (freePct < 8) f.Add(new("Warning","Storage",$"Low space on {drive.Name}",$"Only {freePct:0.0}% free.","Free space to reduce update, cache and paging failures."));
            }
            var startup = CountStartupEntries();
            if (startup > 18) f.Add(new("Info","Startup","Heavy startup set",$"{startup} startup entries detected.","D7 can review startup impact instead of disabling services blindly."));
            if (f.Count == 0) f.Add(new("OK","System","No critical issue detected","Quick diagnostic did not find an obvious fault.","Run a game/benchmark for load-based diagnosis."));
            return f;
        });
    }
    private static int CountStartupEntries()
    {
        int c = 0;
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        foreach (var p in new[] { @"Software\Microsoft\Windows\CurrentVersion\Run", @"Software\Microsoft\Windows\CurrentVersion\RunOnce" })
            try { using var k = hive.OpenSubKey(p); c += k?.GetValueNames().Length ?? 0; } catch { }
        return c;
    }
}
