using Yukti.Application.Abstractions;
using Yukti.Domain.Auditing;

namespace Yukti.Infrastructure;

/// <summary>
/// Commits immediately rather than via IUnitOfWork, same reasoning as
/// EfIdempotencyStore: an audit entry is not domain state participating in
/// an aggregate's save/commit cycle, and AuditableCommandHandler appends on
/// both the success AND failure path — it must persist independent of
/// whatever the command's own transaction did.
/// </summary>
public sealed class EfAuditRepository : IAuditRepository
{
    private readonly YuktiDbContext _context;
    public EfAuditRepository(YuktiDbContext context) => _context = context;

    public async Task Append(AuditEntry entry, CancellationToken ct)
    {
        _context.Add(entry);
        await _context.SaveChangesAsync(ct);
    }
}
