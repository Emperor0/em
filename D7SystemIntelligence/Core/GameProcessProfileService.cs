using System.Diagnostics;

namespace D7SystemIntelligence.Core;

public sealed record GameProcessProfileResult(
    bool Found,
    bool Changed,
    bool Verified,
    int ProcessId,
    string ProcessName,
    string Detail);

public sealed class GameProcessProfileService
{
    private int? _pid;
    private ProcessPriorityClass? _originalPriority;

    public GameProcessProfileResult ApplyCompetitive(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return new(false, false, false, 0, string.Empty, "لا توجد لعبة مؤكدة شغالة الآن؛ لم يطبق D7KT Process tweak على عملية عشوائية.");

        var normalized = Path.GetFileNameWithoutExtension(processName.Trim());
        Process? process = null;
        try
        {
            process = Process.GetProcessesByName(normalized)
                .OrderByDescending(SafeWorkingSet)
                .FirstOrDefault();
            if (process == null)
                return new(false, false, false, 0, normalized, $"تعذر العثور على عملية اللعبة {normalized} الآن.");

            var current = process.PriorityClass;
            if (current is ProcessPriorityClass.AboveNormal or ProcessPriorityClass.High or ProcessPriorityClass.RealTime)
            {
                return new(true, false, true, process.Id, process.ProcessName,
                    $"Already Optimal • {process.ProcessName} priority = {current}. لم يرفع D7KT العملية بلا داعٍ.");
            }

            _pid = process.Id;
            _originalPriority = current;
            process.PriorityClass = ProcessPriorityClass.AboveNormal;
            var verified = process.PriorityClass == ProcessPriorityClass.AboveNormal;
            if (!verified)
            {
                _pid = null;
                _originalPriority = null;
                return new(true, false, false, process.Id, process.ProcessName,
                    $"Windows لم يثبت تغيير Priority لـ{process.ProcessName}.");
            }

            return new(true, true, true, process.Id, process.ProcessName,
                $"Applied + Verified • {process.ProcessName}: {current} → AboveNormal.");
        }
        catch (Exception ex)
        {
            return new(process != null, false, false, process?.Id ?? 0, process?.ProcessName ?? normalized,
                "تعذر تطبيق Game Process Profile: " + ex.Message);
        }
        finally
        {
            process?.Dispose();
        }
    }

    public string Restore()
    {
        if (!_pid.HasValue || !_originalPriority.HasValue)
            return "لا يوجد Game Process change مملوك لـD7KT يحتاج استعادة.";

        try
        {
            using var process = Process.GetProcessById(_pid.Value);
            if (process.HasExited)
                return "انتهت عملية اللعبة؛ لا توجد Priority تحتاج استعادة.";

            process.PriorityClass = _originalPriority.Value;
            var ok = process.PriorityClass == _originalPriority.Value;
            return ok
                ? $"Restore Verified • {process.ProcessName} → {_originalPriority.Value}."
                : "تعذر التحقق من استعادة Priority اللعبة.";
        }
        catch (Exception ex)
        {
            return "تعذر استعادة Game Process Profile: " + ex.Message;
        }
        finally
        {
            _pid = null;
            _originalPriority = null;
        }
    }

    private static long SafeWorkingSet(Process process)
    {
        try { return process.WorkingSet64; }
        catch { return 0; }
    }
}
