using System.Diagnostics;
using System.Text.Json;
using D7.Optimization.Contracts;

namespace D7.Optimization.Operations;

public sealed class ProcessPriorityOptimization : IReversibleOptimizationOperation
{
    private sealed record PriorityState(string PriorityClass);

    public string Id => "process.priority.above_normal";
    public string NameAr => "رفع أولوية اللعبة مؤقتًا";
    public string Risk => "LOW";

    public Task<CapturedOperationState> CaptureStateAsync(OperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.GetProcessById(context.ProcessId);
        var before = new PriorityState(process.PriorityClass.ToString());
        return Task.FromResult(new CapturedOperationState(
            "process-priority",
            context.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(before)));
    }

    public Task<OperationApplyResult> ApplyAsync(OperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var process = Process.GetProcessById(context.ProcessId);
            var current = process.PriorityClass;

            if (current is ProcessPriorityClass.High or ProcessPriorityClass.RealTime)
            {
                return Task.FromResult(new OperationApplyResult(
                    false,
                    true,
                    JsonSerializer.Serialize(new PriorityState(current.ToString())),
                    "أولوية اللعبة مرتفعة مسبقًا، لذلك لم يغير D7 الإعداد."));
            }

            if (current == ProcessPriorityClass.AboveNormal)
            {
                return Task.FromResult(new OperationApplyResult(
                    false,
                    true,
                    JsonSerializer.Serialize(new PriorityState(current.ToString())),
                    "أولوية اللعبة مضبوطة مسبقًا على فوق العادي."));
            }

            process.PriorityClass = ProcessPriorityClass.AboveNormal;
            process.Refresh();
            var verified = process.PriorityClass == ProcessPriorityClass.AboveNormal;
            return Task.FromResult(new OperationApplyResult(
                verified,
                verified,
                JsonSerializer.Serialize(new PriorityState(process.PriorityClass.ToString())),
                verified ? "تم رفع أولوية اللعبة مؤقتًا للاختبار." : "تعذر التحقق من تغيير أولوية اللعبة.",
                verified ? null : "Priority verification failed"));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return Task.FromResult(new OperationApplyResult(
                false,
                false,
                "{}",
                "تعذر تطبيق تجربة أولوية اللعبة بأمان.",
                ex.Message));
        }
    }

    public Task<bool> RollbackAsync(OperationContext context, CapturedOperationState capturedState, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var state = JsonSerializer.Deserialize<PriorityState>(capturedState.BeforeJson);
            if (state is null || !Enum.TryParse<ProcessPriorityClass>(state.PriorityClass, ignoreCase: true, out var priority)) return Task.FromResult(false);

            using var process = Process.GetProcessById(context.ProcessId);
            process.PriorityClass = priority;
            process.Refresh();
            return Task.FromResult(process.PriorityClass == priority);
        }
        catch (ArgumentException)
        {
            // If the game process exited, Windows removed the temporary priority state automatically.
            return Task.FromResult(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return Task.FromResult(false);
        }
    }
}
