using System.Diagnostics;
using Microsoft.Win32;

namespace D7SystemIntelligence.Core;

public sealed class DiagnosticsEngine
{
    public async Task<List<DiagnosticFinding>> RunAsync(HardwareSnapshot hw)
    {
        return await Task.Run(() =>
        {
            var findings = new List<DiagnosticFinding>();

            if (hw.CpuTemp >= 90)
                findings.Add(new(
                    "حرج", "الحرارة", "حرارة المعالج مرتفعة جدًا",
                    $"وصل المعالج إلى {hw.CpuTemp:0}°C في آخر Snapshot.",
                    "أوقف أي ضغط غير ضروري وافحص المشتت والمعجون وتدفق الهواء. D7KT لا يرفع المراوح بالقوة إذا ما عنده قناة PWM writable.",
                    "THERMAL_CPU_CRITICAL",
                    $"CPU={hw.CpuName} • Temp={hw.CpuTemp:0.0}C • Load={hw.CpuLoad:0.0}%"));
            else if (hw.CpuTemp >= 82)
                findings.Add(new(
                    "تحذير", "الحرارة", "حرارة المعالج مرتفعة",
                    $"حرارة المعالج الحالية {hw.CpuTemp:0}°C.",
                    "راقب الحرارة تحت نفس الحمل وافتح التحكم الحراري لمعرفة هل المراوح قابلة للتحكم أو قراءة فقط.",
                    "THERMAL_CPU_WARNING",
                    $"CPU={hw.CpuName} • Temp={hw.CpuTemp:0.0}C • Load={hw.CpuLoad:0.0}%"));

            if (hw.GpuTemp >= 86)
                findings.Add(new(
                    "تحذير", "الحرارة", "حرارة كرت الشاشة مرتفعة",
                    $"حرارة كرت الشاشة الحالية {hw.GpuTemp:0}°C.",
                    "افحص تبريد الكرت وتدفق الهواء، ثم قارن نفس المشهد قبل/بعد أي تعديل بدل خفض الإعدادات عشوائيًا.",
                    "THERMAL_GPU_WARNING",
                    $"GPU={hw.GpuName} • Temp={hw.GpuTemp:0.0}C • Load={hw.GpuLoad:0.0}%"));

            if (hw.RamLoad >= 88)
                findings.Add(new(
                    "تحذير", "الذاكرة", "ضغط RAM مرتفع",
                    $"استخدام الذاكرة وصل إلى {hw.RamLoad:0}%.",
                    "افتح Background Apps لمعرفة العمليات الفعلية المستهلكة. Smart Clean يغلق فقط ما هو مصنف Safe-To-Close.",
                    "RAM_PRESSURE",
                    $"RAM Load={hw.RamLoad:0.0}%"));

            if ((hw.VramLoad ?? 0) >= 92)
                findings.Add(new(
                    "تحذير", "ذاكرة الكرت", "ضغط VRAM مرتفع",
                    $"استخدام VRAM وصل إلى {hw.VramLoad:0}%.",
                    "VRAM ليست RAM عادية؛ D7KT لن يغلق عمليات أو يغير إعدادات لعبة بشكل أعمى. راجع اللعبة/الخامات والـStreaming Budget.",
                    "VRAM_PRESSURE",
                    $"VRAM Load={hw.VramLoad:0.0}% • GPU={hw.GpuName}"));

            try
            {
                using var log = new EventLog("System");
                var recent = log.Entries.Cast<EventLogEntry>()
                    .Reverse()
                    .Take(1600)
                    .Where(e => e.TimeGenerated > DateTime.Now.AddDays(-3))
                    .ToArray();

                var wheaEntries = recent.Where(e => e.Source.Contains("WHEA", StringComparison.OrdinalIgnoreCase)).ToArray();
                var diskEntries = recent.Where(e => e.Source.Contains("disk", StringComparison.OrdinalIgnoreCase) && e.EntryType == EventLogEntryType.Error).ToArray();
                var nvEntries = recent.Where(e => e.Source.Contains("nvlddmkm", StringComparison.OrdinalIgnoreCase)).ToArray();

                if (wheaEntries.Length > 0)
                {
                    var last = wheaEntries[0];
                    findings.Add(new(
                        "حرج", "الاستقرار", "تم العثور على أخطاء WHEA",
                        $"تم رصد {wheaEntries.Length} سجل WHEA خلال آخر 3 أيام. آخر سجل {last.TimeGenerated:g}.",
                        "لا يوجد زر إصلاح آمن عام لـWHEA. افتح Crash Investigator واربط الحدث بالـOC/الحرارة/الطاقة ثم ارجع لآخر إعداد مستقر إذا ثبت الارتباط.",
                        "WHEA_RECENT",
                        $"Source={last.Source} • EventID={last.InstanceId} • Time={last.TimeGenerated:O}"));
                }

                if (nvEntries.Length > 0)
                {
                    var last = nvEntries[0];
                    findings.Add(new(
                        "تحذير", "تعريف كرت الشاشة", "تم العثور على أخطاء NVIDIA",
                        $"تم رصد {nvEntries.Length} سجل nvlddmkm خلال آخر 3 أيام. آخر سجل {last.TimeGenerated:g}.",
                        "افتح Driver Safety واربط الخطأ بتاريخ التعريف وكسر سرعة GPU. لا يفترض D7KT أن أحدث تعريف هو الأفضل.",
                        "NVIDIA_DRIVER_EVENTS",
                        $"Source={last.Source} • EventID={last.InstanceId} • Time={last.TimeGenerated:O}"));
                }

                if (diskEntries.Length > 0)
                {
                    var last = diskEntries[0];
                    findings.Add(new(
                        "تحذير", "التخزين", "تم العثور على أخطاء Disk",
                        $"تم رصد {diskEntries.Length} خطأ Disk خلال آخر 3 أيام. آخر سجل {last.TimeGenerated:g}.",
                        "ابدأ بفحص CHKDSK /scan وStorage Health. الإصلاح Offline لا ينفذ تلقائيًا لأنه قد يحتاج إعادة تشغيل ومراجعة حالة القرص أولًا.",
                        "DISK_EVENT_ERRORS",
                        $"Source={last.Source} • EventID={last.InstanceId} • Time={last.TimeGenerated:O}"));
                }
            }
            catch (Exception ex)
            {
                findings.Add(new(
                    "معلومة", "التشخيص", "تعذر قراءة System Event Log",
                    "D7KT أكمل بقية الفحص، لكن Event Log evidence غير متاح في هذه الجولة.",
                    "تحقق من صلاحيات التطبيق وخدمة Windows Event Log.",
                    "EVENTLOG_UNAVAILABLE",
                    ex.Message));
            }

            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                var freePct = drive.TotalSize == 0 ? 100 : drive.TotalFreeSpace * 100.0 / drive.TotalSize;
                if (freePct < 8)
                {
                    var freeGb = drive.TotalFreeSpace / 1024d / 1024 / 1024;
                    findings.Add(new(
                        "تحذير", "التخزين", $"المساحة منخفضة في {drive.Name}",
                        $"المتاح {freeGb:0.0} GB فقط ({freePct:0.0}%).",
                        "يمكن لـD7KT تنظيف Temp القديمة الآمنة، لكن Downloads وملفات المستخدم لا تُحذف تلقائيًا.",
                        "DISK_LOW_SPACE",
                        $"Drive={drive.Name} • Free={freeGb:0.00}GB • FreePct={freePct:0.00}%"));
                }
            }

            var startup = CountStartupEntries();
            if (startup > 18)
                findings.Add(new(
                    "معلومة", "بدء التشغيل", "عدد برامج بدء التشغيل مرتفع",
                    $"تم اكتشاف {startup} عنصر Run/RunOnce.",
                    "افتح Startup Manager؛ D7KT لا يعطل عناصر لمجرد أن العدد كبير، بل يحتاج قرارًا لكل عنصر.",
                    "STARTUP_HIGH_COUNT",
                    $"Run/RunOnce count={startup}"));

            if (findings.Count == 0)
                findings.Add(new(
                    "سليم", "النظام", "لا توجد مشكلة حرجة ظاهرة",
                    "الفحص الحالي لم يجد Threshold أو Event evidence يستدعي إجراء.",
                    "للتشخيص تحت الحمل، افتح لعبة وانتظر Session telemetry ثم أعد الفحص.",
                    "HEALTH_OK",
                    $"CPU {hw.CpuLoad:0}%/{hw.CpuTemp:0}C • GPU {hw.GpuLoad:0}%/{hw.GpuTemp:0}C • RAM {hw.RamLoad:0}%"));

            return findings;
        });
    }

    private static int CountStartupEntries()
    {
        var count = 0;
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        foreach (var path in new[] { @"Software\Microsoft\Windows\CurrentVersion\Run", @"Software\Microsoft\Windows\CurrentVersion\RunOnce" })
            try
            {
                using var key = hive.OpenSubKey(path);
                count += key?.GetValueNames().Length ?? 0;
            }
            catch { }
        return count;
    }
}
