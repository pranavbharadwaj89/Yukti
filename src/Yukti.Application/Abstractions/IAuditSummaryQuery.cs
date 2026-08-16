using Yukti.Domain.SharedKernel;

namespace Yukti.Application.Abstractions;

// FR-REPO-02 split — IAuditRepository (IAuditRepository.cs) only has
// Append; list operations live here. Metadata is deliberately excluded
// from the summary (may carry step params/context not meant for a
// blanket list view) — GetById below is that later, separate detail-fetch.
public sealed record AuditEntrySummary(
    AuditEntryId Id, string CommandType, TenantId? TenantId, bool Succeeded, string? FailureReason, DateTimeOffset OccurredAt);

// The detail view: same fields as the summary plus Metadata, fetched one
// entry at a time so the blanket list endpoint never pays for it.
public sealed record AuditEntryDetail(
    AuditEntryId Id, string CommandType, TenantId? TenantId, bool Succeeded, string? FailureReason,
    IReadOnlyDictionary<string, object?> Metadata, DateTimeOffset OccurredAt);

public interface IAuditSummaryQuery
{
    Task<IReadOnlyList<AuditEntrySummary>> ListByTenant(TenantId tenantId, CancellationToken ct);

    // Null if the entry doesn't exist or belongs to a different tenant —
    // callers can't distinguish the two, matching FR-TENANT-01's usual
    // "not found" response for cross-tenant reads (no existence leak).
    Task<AuditEntryDetail?> GetById(AuditEntryId id, TenantId tenantId, CancellationToken ct);
}
