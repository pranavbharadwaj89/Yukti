using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.SharedKernel;

namespace Yukti.Application.Abstractions;

/// <summary>FR-CQRS-01: a query-side read model, deliberately not a
/// repository method (FR-REPO-02 bans GetAll/list from repositories) —
/// this queries the exact same flows table a write just landed in, so
/// "read your own writes" holds with zero synchronization machinery: there
/// is no second copy of the data to fall behind.</summary>
public sealed record FlowSummaryReadModel(FlowId FlowId, string Name, FlowStatus Status, int Version);

public interface IFlowSummaryQuery
{
    Task<IReadOnlyList<FlowSummaryReadModel>> ListByTenant(TenantId tenantId, CancellationToken ct);
}
