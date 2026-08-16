using Yukti.Domain.SharedKernel;

namespace Yukti.Application.Abstractions;

/// <summary>
/// FR-CQRS-01-style read model, mirroring IFlowSummaryQuery — but unlike
/// Flows (where the list view only needs name/status/version), Explorer's
/// tree UX genuinely needs every saved request's full fields up front (no
/// separate per-request detail fetch on click), so this returns the whole
/// collection->requests tree rather than a flatter summary shape.
/// </summary>
public sealed record ApiRequestSummary(
    ApiRequestId Id,
    string Name,
    string Method,
    string Url,
    IReadOnlyDictionary<string, object?> Headers,
    IReadOnlyDictionary<string, object?> QueryParams,
    object? Body,
    object? Assertions,
    int Order);

public sealed record ApiCollectionSummary(
    ApiCollectionId Id,
    string Name,
    string? Description,
    IReadOnlyList<ApiRequestSummary> Requests,
    ProjectId? ProjectId = null);

public interface IApiCollectionSummaryQuery
{
    Task<IReadOnlyList<ApiCollectionSummary>> ListByTenant(TenantId tenantId, CancellationToken ct);
}
