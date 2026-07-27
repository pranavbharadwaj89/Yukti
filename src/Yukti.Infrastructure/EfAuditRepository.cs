using Yukti.Application.Abstractions;
using Yukti.Domain.Auditing;

namespace Yukti.Infrastructure;

/// <summary>
/// FR-OPS-03: stages onto the same shared, request-scoped DbContext the
/// command's own business state uses — no SaveChangesAsync here.
/// AuditableCommandHandler commits both together in one round trip via
/// IUnitOfWork.Commit(), on both the success AND failure path (see its own
/// doc comment for the failure path's DiscardStaged() call, which is what
/// keeps a half-done business mutation from riding along with a failure's
/// audit entry).
/// </summary>
public sealed class EfAuditRepository : IAuditRepository
{
    private readonly YuktiDbContext _context;
    public EfAuditRepository(YuktiDbContext context) => _context = context;

    public Task Append(AuditEntry entry, CancellationToken ct)
    {
        _context.Add(entry);
        return Task.CompletedTask;
    }
}
