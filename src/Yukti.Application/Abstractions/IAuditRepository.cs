using Yukti.Domain.Auditing;

namespace Yukti.Application.Abstractions;

/// <summary>
/// FR-AUDIT-03/04: append-only by contract — deliberately exposes no
/// Update/Delete/Get. A real implementation's backing table grants its
/// application DB role INSERT/SELECT only (see the AddAuditEntries
/// migration), but the interface itself already makes mutation
/// unreachable from application code, independent of the DB grant.
///
/// FR-OPS-03: Append no longer guarantees the entry is durably committed
/// the moment it returns — it stages the entry into the ambient unit of
/// work (a real DB round trip saved per command, not a style change).
/// AuditableCommandHandler is the only caller and always follows with
/// IUnitOfWork.Commit() in the same Handle() call, so the entry is still
/// durably persisted before that method returns to its own caller.
/// </summary>
public interface IAuditRepository
{
    Task Append(AuditEntry entry, CancellationToken ct);
}
