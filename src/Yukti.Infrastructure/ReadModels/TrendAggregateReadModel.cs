using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure.ReadModels;

/// <summary>
/// FR-CQRS-03: recomputed by TrendAggregateBatchJob on a fixed cadence,
/// never per-event — LastUpdatedAt is the staleness bound every consuming
/// query must surface alongside the numbers, per the FR's own wording.
/// One row per tenant (the trailing-24h window recomputed each pass).
/// </summary>
public sealed class TrendAggregateReadModel
{
    public required TenantId TenantId { get; init; }
    public required int TotalRunsLast24h { get; init; }
    public required double PassRateLast24h { get; init; }
    public required double FlakeRateLast24h { get; init; }
    public required DateTimeOffset LastUpdatedAt { get; init; }
}
