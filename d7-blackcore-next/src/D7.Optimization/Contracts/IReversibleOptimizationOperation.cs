namespace D7.Optimization.Contracts;

public sealed record OperationContext(
    int ProcessId,
    string ProcessName,
    string? ExecutablePath);

public sealed record OperationPreflightResult(
    bool Applicable,
    string MessageAr,
    string? TechnicalReason = null);

public sealed record CapturedOperationState(
    string Kind,
    string Target,
    string BeforeJson);

public sealed record OperationApplyResult(
    bool Applied,
    bool Verified,
    string AfterJson,
    string MessageAr,
    string? Error = null);

public interface IReversibleOptimizationOperation
{
    string Id { get; }
    string NameAr { get; }
    string Risk { get; }

    Task<OperationPreflightResult> PreflightAsync(OperationContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new OperationPreflightResult(true, "العملية قابلة للاختبار."));

    Task<CapturedOperationState> CaptureStateAsync(OperationContext context, CancellationToken cancellationToken);
    Task<OperationApplyResult> ApplyAsync(OperationContext context, CancellationToken cancellationToken);
    Task<bool> RollbackAsync(OperationContext context, CapturedOperationState capturedState, CancellationToken cancellationToken);
}
