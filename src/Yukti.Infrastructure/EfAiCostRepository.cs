using Yukti.Application.Abstractions;
using Yukti.Domain.Ai;

namespace Yukti.Infrastructure;

/// <summary>
/// D5-T9: unlike <see cref="EfAuditRepository"/> (stages onto the shared request-scoped DbContext,
/// committed by whatever unit of work wraps the call), this one commits itself, immediately.
/// §R5's own guarantee is "every provider call writes a row" — including every failed attempt
/// before an eventual <c>AiGenerationFailedException</c>, and every budget refusal before it's
/// even thrown. Both of those unwind through <see cref="Auditing.AuditableCommandHandler{TCommand,TResult}"/>'s
/// failure path, which calls <c>IUnitOfWork.DiscardStaged()</c> (<c>ChangeTracker.Clear()</c>) —
/// deliberately, to stop a half-done business mutation riding along with a failure's audit entry
/// (<see cref="EfAuditRepository"/>'s own doc comment). A cost record staged on that same shared
/// context would be wiped by that same Clear() before anyone ever saved it, silently undercounting
/// real spend on every failure — exactly the case the budget guard most needs to count correctly.
/// Cost rows have no FK to the business aggregate and no consistency reason to share that
/// transaction, so they save through on their own instead.
/// </summary>
public sealed class EfAiCostRepository : IAiCostRepository
{
    private readonly YuktiDbContext _context;
    public EfAiCostRepository(YuktiDbContext context) => _context = context;

    public async Task Append(AiCostRecord record, CancellationToken ct)
    {
        _context.Add(record);
        await _context.SaveChangesAsync(ct);
    }
}
