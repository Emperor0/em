namespace D7.Rollback.Models;

public enum TransactionStatus
{
    Started,
    Applying,
    Committed,
    RollingBack,
    RolledBack,
    Failed
}

public sealed record OperationJournalEntry(
    string OperationId,
    string Kind,
    string Target,
    string? BeforeJson,
    string? AfterJson,
    bool Applied,
    bool Verified,
    bool RolledBack,
    string? Error,
    DateTimeOffset UpdatedAt);

public sealed record OptimizationTransaction(
    string TransactionId,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    TransactionStatus Status,
    string ReasonAr,
    IReadOnlyList<OperationJournalEntry> Operations,
    string? Error = null);
