using Microsoft.EntityFrameworkCore;
using Yukti.Application.Abstractions;
using Yukti.Domain.Execution;
using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure;

/// <summary>
/// Not a domain aggregate — pure infrastructure bookkeeping for FR-API-02,
/// so it lives directly in Yukti.Infrastructure rather than Yukti.Domain.
/// Composite primary key (TenantId, Key) is the natural key: it IS the
/// uniqueness guarantee the whole feature depends on — a second Record()
/// call with the same (tenant, key) violates the PK and fails, which is
/// exactly the "never a second execution" behavior FR-API-02 wants (the
/// caller is expected to check TryGetResult first, as the run-trigger
/// endpoint does).
/// </summary>
public sealed class IdempotencyRecord
{
    public required Guid TenantId { get; init; }
    public required string Key { get; init; }
    public required Guid FlowRunId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed class EfIdempotencyStore : IIdempotencyStore
{
    private readonly YuktiDbContext _context;
    public EfIdempotencyStore(YuktiDbContext context) => _context = context;

    public async Task<FlowRunId?> TryGetResult(TenantId tenantId, string idempotencyKey, CancellationToken ct)
    {
        var record = await _context.Set<IdempotencyRecord>()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId.Value && r.Key == idempotencyKey, ct);
        return record is null ? null : new FlowRunId(record.FlowRunId);
    }

    // Commits immediately rather than via IUnitOfWork — this is a
    // side-channel dedup record, not domain state, so it doesn't
    // participate in FlowEngine's per-step commit/event-dispatch pattern.
    public async Task Record(TenantId tenantId, string idempotencyKey, FlowRunId runId, CancellationToken ct)
    {
        _context.Add(new IdempotencyRecord
        {
            TenantId = tenantId.Value,
            Key = idempotencyKey,
            FlowRunId = runId.Value,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _context.SaveChangesAsync(ct);
    }
}
