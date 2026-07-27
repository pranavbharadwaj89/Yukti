using Yukti.Application.Abstractions;
using Yukti.Domain.Events;

namespace Yukti.Infrastructure.ReadModels;

/// <summary>
/// FR-CQRS-02 + FR-EVT-02: projects FlowRunCompletedEvent into
/// FlowReportReadModel — upserts by FlowRunId (the row's own primary key),
/// never a blind insert, so at-least-once redelivery from the outbox
/// relay produces the same end state, not duplicate/inconsistent rows.
/// </summary>
public sealed class FlowReportProjectionConsumer : ITier2EventConsumer<FlowRunCompletedEvent>
{
    private readonly YuktiDbContext _context;
    private readonly IFlowRunRepository _runs;

    public FlowReportProjectionConsumer(YuktiDbContext context, IFlowRunRepository runs)
    {
        _context = context;
        _runs = runs;
    }

    public async Task Handle(FlowRunCompletedEvent domainEvent, CancellationToken ct)
    {
        var run = await _runs.GetById(domainEvent.RunId, ct);
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
