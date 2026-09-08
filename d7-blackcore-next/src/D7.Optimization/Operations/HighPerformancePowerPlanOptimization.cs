using System.Runtime.InteropServices;
using System.Text.Json;
using D7.Optimization.Contracts;

namespace D7.Optimization.Operations;

public sealed class HighPerformancePowerPlanOptimization : IReversibleOptimizationOperation
{
    private static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private sealed record PowerPlanState(Guid SchemeGuid);

    public string Id => "windows.power_plan.high_performance";
    public string NameAr => "خطة الطاقة عالية الأداء";
    public string Risk => "LOW";

    public Task<CapturedOperationState> CaptureStateAsync(OperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = GetActiveScheme();
        return Task.FromResult(new CapturedOperationState(
            "power-plan",
            "current-user",
            JsonSerializer.Serialize(new PowerPlanState(current))));
    }

    public Task<OperationApplyResult> ApplyAsync(OperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var current = GetActiveScheme();
            if (current == HighPerformance)
            {
                return Task.FromResult(new OperationApplyResult(
                    false,
                    true,
                    JsonSerializer.Serialize(new PowerPlanState(current)),
                    "خطة الطاقة عالية الأداء مفعلة مسبقًا، لذلك لم يغير D7 شيئًا."));
            }

            var target = HighPerformance;
            var result = PowerSetActiveScheme(IntPtr.Zero, ref target);
            if (result != 0)
            {
                return Task.FromResult(new OperationApplyResult(
                    false,
                    false,
                    JsonSerializer.Serialize(new PowerPlanState(current)),
                    "تعذر تفعيل خطة الطاقة عالية الأداء بأمان.",
                    $"PowerSetActiveScheme returned {result}."));
            }

            var active = GetActiveScheme();
            var verified = active == HighPerformance;
            return Task.FromResult(new OperationApplyResult(
                verified,
                verified,
                JsonSerializer.Serialize(new PowerPlanState(active)),
                verified ? "تم تفعيل خطة الطاقة عالية الأداء مؤقتًا للاختبار." : "تعذر التحقق من خطة الطاقة الجديدة.",
                verified ? null : "Power plan verification failed."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new OperationApplyResult(
                false,
                false,
                "{}",
                "تعذر تنفيذ تجربة خطة الطاقة.",
                ex.Message));
        }
    }

    public Task<bool> RollbackAsync(OperationContext context, CapturedOperationState capturedState, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var state = JsonSerializer.Deserialize<PowerPlanState>(capturedState.BeforeJson);
            if (state is null) return Task.FromResult(false);
            var original = state.SchemeGuid;
            var result = PowerSetActiveScheme(IntPtr.Zero, ref original);
            return Task.FromResult(result == 0 && GetActiveScheme() == original);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public static Guid HighPerformanceSchemeGuid => HighPerformance;

    private static Guid GetActiveScheme()
    {
        var result = PowerGetActiveScheme(IntPtr.Zero, out var pointer);
        if (result != 0 || pointer == IntPtr.Zero)
            throw new InvalidOperationException($"PowerGetActiveScheme returned {result}.");

        try
        {
            return Marshal.PtrToStructure<Guid>(pointer);
        }
        finally
        {
            _ = LocalFree(pointer);
        }
    }

    [DllImport("powrprof.dll", SetLastError = false)]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll", SetLastError = false)]
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
