using Microsoft.EntityFrameworkCore;
using Yukti.Application.Abstractions;
using Yukti.Domain.Events;

namespace Yukti.Infrastructure.ReadModels;

/// <summary>
/// FR-CQRS-02 + FR-EVT-02: projects FlowRunCompletedEvent into
/// FlowReportReadModel — upserts by FlowRunId (the row's own primary key),
/// never a blind insert, so at-least-once redelivery from the outbox
/// relay produces the same end state, not duplicate/inconsistent rows.
///
/// Looks the FlowRun up via a direct, untenanted DbContext query rather
/// than IFlowRunRepository: this consumer runs inside the outbox relay's
/// background scope, which has no ambient tenant (no HttpContext, no JWT)
/// to satisfy IFlowRunRepository's own tenant filter — using it here meant
/// the lookup always returned null and this projection silently never
/// wrote a single row. The Tier 2 relay is legitimately cross-tenant by
/// design (one relay processes every tenant's events), so bypassing the
/// per-tenant filter here is correct, not a shortcut.
/// </summary>
public sealed class FlowReportProjectionConsumer : ITier2EventConsumer<FlowRunCompletedEvent>
{
    private readonly YuktiDbContext _context;

    public FlowReportProjectionConsumer(YuktiDbContext context)
    {
        _context = context;
    }

    public async Task Handle(FlowRunCompletedEvent domainEvent, CancellationToken ct)
    {
        var run = await _context.FlowRuns.FirstOrDefaultAsync(r => r.Id == domainEvent.RunId, ct);
        if (run is null)
            return; // run was somehow removed between the event firing and this projection — nothing to report on

        var existing = await _context.Set<FlowReportReadModel>().FindAsync(new object[] { domainEvent.RunId }, ct);
        if (existing is not null)
            _context.Remove(existing); // upsert-by-key: replace, never duplicate-insert

        _context.Add(new FlowReportReadModel
        {
            FlowRunId = domainEvent.RunId,
            FlowId = run.FlowId,
            TenantId = run.TenantId,
            FinalStatus = domainEvent.FinalStatus,
            PassedCount = domainEvent.PassedCount,
            FailedCount = domainEvent.FailedCount,
            SkippedCount = domainEvent.SkippedCount,
            TotalDuration = domainEvent.TotalDuration,
            OccurredAt = domainEvent.OccurredAt,
            ProjectedAt = DateTimeOffset.UtcNow,
        });

        await _context.SaveChangesAsync(ct);
    }
}
