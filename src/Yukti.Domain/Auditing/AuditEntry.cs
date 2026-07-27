using Yukti.Domain.SharedKernel;

namespace Yukti.Domain.Auditing;

/// <summary>
/// FR-AUDIT (Volume 1 Part IV §27): one entry per command handler
/// invocation, capturing what was attempted and whether it succeeded —
/// append-only by design (no method here mutates an existing entry, and
/// no repository interface exposes an Update/Delete). Not an
/// AggregateRoot: an audit entry raises no domain events and is never
/// re-loaded/mutated after creation, so the extra machinery would be
/// pure overhead. (Volume 1 Part IV §27.2-27.6)
/// </summary>
public sealed class AuditEntry
{
    public AuditEntryId Id { get; }
    public string CommandType { get; }
    public bool Succeeded { get; }
    public string? FailureReason { get; }
    public IReadOnlyDictionary<string, object?> Metadata { get; }
    public DateTimeOffset OccurredAt { get; }

    private AuditEntry(AuditEntryId id, string commandType, bool succeeded, string? failureReason,
        IReadOnlyDictionary<string, object?> metadata, DateTimeOffset occurredAt)
    {
        Id = id;
        CommandType = commandType;
        Succeeded = succeeded;
        FailureReason = failureReason;
        Metadata = metadata;
        OccurredAt = occurredAt;
    }

    public static AuditEntry Capture(string commandType, bool succeeded, string? failureReason,
        IReadOnlyDictionary<string, object?> metadata) =>
        new(AuditEntryId.New(), commandType, succeeded, failureReason, metadata, DateTimeOffset.UtcNow);
}
