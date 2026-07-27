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
    // FR-DB-02: nullable because not every command runs with tenant context
    // established yet (e.g. RegisterUserCommand's self-registration mints
    // a brand-new TenantId as part of the command itself, and process-
    // startup seeding runs with no HTTP request at all) — RLS's own policy
    // handles NULL the same permissive way roles/module_registrations do.
    public TenantId? TenantId { get; }
    public bool Succeeded { get; }
    public string? FailureReason { get; }
    public IReadOnlyDictionary<string, object?> Metadata { get; }
    public DateTimeOffset OccurredAt { get; }

    private AuditEntry(AuditEntryId id, string commandType, TenantId? tenantId, bool succeeded, string? failureReason,
        IReadOnlyDictionary<string, object?> metadata, DateTimeOffset occurredAt)
    {
        Id = id;
        CommandType = commandType;
        TenantId = tenantId;
        Succeeded = succeeded;
        FailureReason = failureReason;
        Metadata = metadata;
        OccurredAt = occurredAt;
    }

    public static AuditEntry Capture(string commandType, TenantId? tenantId, bool succeeded, string? failureReason,
        IReadOnlyDictionary<string, object?> metadata) =>
        new(AuditEntryId.New(), commandType, tenantId, succeeded, failureReason, metadata, DateTimeOffset.UtcNow);
}
