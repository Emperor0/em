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

            if (hw.CpuTemp >= 90)
                f.Add(new("حرج","الحرارة","حرارة المعالج مرتفعة جدًا",$"وصل المعالج إلى {hw.CpuTemp:0}°C.","افحص تركيب المشتت والمعجون وتدفق الهواء ومنحنى المراوح."));
            else if (hw.CpuTemp >= 82)
                f.Add(new("تحذير","الحرارة","حرارة المعالج مرتفعة",$"حرارة المعالج الحالية {hw.CpuTemp:0}°C.","راقب الحمل المستمر ومنحنى المراوح."));

            if (hw.GpuTemp >= 86)
                f.Add(new("تحذير","الحرارة","حرارة كرت الشاشة مرتفعة",$"حرارة كرت الشاشة الحالية {hw.GpuTemp:0}°C.","افحص تبريد الكرت وتدفق الهواء داخل الكيس."));

            if (hw.RamLoad >= 88)
                f.Add(new("تحذير","الذاكرة","ضغط RAM مرتفع",$"استخدام الذاكرة وصل إلى {hw.RamLoad:0}%.","أغلق البرامج غير المهمة أو ارفع سعة الذاكرة إذا تكرر الضغط أثناء اللعب."));

            if ((hw.VramLoad ?? 0) >= 92)
                f.Add(new("تحذير","ذاكرة الكرت","ضغط VRAM مرتفع",$"استخدام VRAM وصل إلى {hw.VramLoad:0}%.","اخفض استهلاك الخامات أو Streaming Budget قبل تخفيض كل الإعدادات."));

            try
            {
                using var log = new EventLog("System");
                var recent = log.Entries.Cast<EventLogEntry>()
                    .Reverse()
                    .Take(1200)
                    .Where(e => e.TimeGenerated > DateTime.Now.AddDays(-3))
                    .ToArray();

                var whea = recent.Count(e => e.Source.Contains("WHEA", StringComparison.OrdinalIgnoreCase));
                var disk = recent.Count(e => e.Source.Contains("disk", StringComparison.OrdinalIgnoreCase) && e.EntryType == EventLogEntryType.Error);
                var nv = recent.Count(e => e.Source.Contains("nvlddmkm", StringComparison.OrdinalIgnoreCase));

                if (whea > 0)
                    f.Add(new("حرج","الاستقرار","تم العثور على أخطاء WHEA",$"تم رصد {whea} سجل WHEA حديث خلال آخر 3 أيام.","ارجع كسر سرعة المعالج أو الذاكرة إلى آخر إعداد مستقر قبل أي تعديل جديد."));

                if (nv > 0)
                    f.Add(new("تحذير","تعريف كرت الشاشة","تم العثور على أخطاء NVIDIA",$"تم رصد {nv} سجل nvlddmkm حديث.","افحص استقرار كسر سرعة الكرت والتعريف الحالي."));

                if (disk > 0)
                    f.Add(new("تحذير","التخزين","تم العثور على أخطاء للقرص",$"تم رصد {disk} خطأ Disk حديث.","افحص SMART والاتصال وسلامة نظام الملفات."));
            }
            catch { }

            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                var freePct = drive.TotalSize == 0 ? 100 : drive.TotalFreeSpace * 100.0 / drive.TotalSize;
                if (freePct < 8)
                    f.Add(new("تحذير","التخزين",$"المساحة منخفضة في {drive.Name}",$"المتاح فقط {freePct:0.0}%.","وفر مساحة لتقليل مشاكل التحديثات والكاش والـPagefile."));
            }

            var startup = CountStartupEntries();
            if (startup > 18)
                f.Add(new("معلومة","بدء التشغيل","عدد برامج بدء التشغيل مرتفع",$"تم اكتشاف {startup} عنصر بدء تشغيل.","D7 يراجع أثرها على الأداء بدل تعطيل الخدمات عشوائيًا."));

            if (f.Count == 0)
                f.Add(new("سليم","النظام","لا توجد مشكلة حرجة ظاهرة","الفحص السريع لم يجد عطلًا واضحًا في حالة الخمول الحالية.","شغّل لعبة أو Benchmark ثم أعد الفحص للحصول على تشخيص تحت الحمل."));

            return f;
        });
    }

    private static int CountStartupEntries()
    {
        var c = 0;
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        foreach (var p in new[] { @"Software\Microsoft\Windows\CurrentVersion\Run", @"Software\Microsoft\Windows\CurrentVersion\RunOnce" })
            try
            {
                using var k = hive.OpenSubKey(p);
                c += k?.GetValueNames().Length ?? 0;
            }
            catch { }
        return c;
    }
}
