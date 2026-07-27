using Yukti.Domain.Execution;
using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure.ReadModels;

/// <summary>
/// FR-CQRS-02: FlowReportReadModel — eventually consistent, projected by
/// FlowReportProjectionConsumer (Tier 2, off FlowRunCompletedEvent), never
/// written synchronously by any command handler. ProjectedAt is the
/// explicit staleness bound every consumer of this model must show
/// alongside the data (FR-CQRS-02's "no code path assumes synchronous
/// consistency" requirement) — the gap between OccurredAt and ProjectedAt
/// is exactly how stale a given row currently is.
/// </summary>
public sealed class FlowReportReadModel
{
    public required FlowRunId FlowRunId { get; init; }
    public required FlowId FlowId { get; init; }
    public required TenantId TenantId { get; init; }
    public required RunStatus FinalStatus { get; init; }
    public required int PassedCount { get; init; }
    public required int FailedCount { get; init; }
    public required int SkippedCount { get; init; }
    public required TimeSpan TotalDuration { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required DateTimeOffset ProjectedAt { get; init; }
}
