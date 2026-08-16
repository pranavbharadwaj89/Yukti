using Yukti.Domain.Execution;
using Yukti.Domain.SharedKernel;

namespace Yukti.Application.Abstractions;

// Reports page per-flow drill-down: aggregates FlowReportReadModel
// (FR-CQRS-02, projected off FlowRunCompletedEvent) by FlowId so the
// tenant-wide trend numbers on the Reports page can be broken down
// per flow without a new read model — the rows already exist, this is
// just a different grouping of the same eventually-consistent data.
public sealed record FlowReportSummary(
    FlowId FlowId, string FlowName, int TotalRuns, int PassedRuns, int FailedRuns,
    DateTimeOffset LastRunAt, RunStatus LastRunStatus);

public sealed record FlowRunReportEntry(
    FlowRunId FlowRunId, RunStatus FinalStatus, int PassedCount, int FailedCount, int SkippedCount,
    TimeSpan TotalDuration, DateTimeOffset OccurredAt, DateTimeOffset ProjectedAt);

public interface IFlowReportSummaryQuery
{
    Task<IReadOnlyList<FlowReportSummary>> ListByTenant(TenantId tenantId, CancellationToken ct);

    // Empty (not an error) if the flow has no reported runs yet or belongs
    // to a different tenant — same "no existence leak" shape as
    // IAuditSummaryQuery.GetById.
    Task<IReadOnlyList<FlowRunReportEntry>> ListRunsByFlow(FlowId flowId, TenantId tenantId, CancellationToken ct);
}
