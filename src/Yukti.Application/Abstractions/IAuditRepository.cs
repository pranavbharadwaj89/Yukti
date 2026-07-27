using Yukti.Domain.Auditing;

namespace Yukti.Application.Abstractions;

/// <summary>
/// FR-AUDIT-03/04: append-only by contract — deliberately exposes no
/// Update/Delete/Get. A real implementation's backing table grants its
/// application DB role INSERT/SELECT only (see the AddAuditEntries
/// migration), but the interface itself already makes mutation
/// unreachable from application code, independent of the DB grant.
/// </summary>
public interface IAuditRepository
{
    Task Append(AuditEntry entry, CancellationToken ct);
}
