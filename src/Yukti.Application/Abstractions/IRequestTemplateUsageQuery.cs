using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.SharedKernel;

namespace Yukti.Application.Abstractions;

/// <summary>
/// API Testing Module Spec, API-T9: "the UI shows which flows use a template, so the blast
/// radius is visible before editing." Backend-only for now — this is the read model a future UI
/// page would call; no such page exists yet (tracked separately, not built this week).
/// </summary>
public sealed record RequestTemplateUsageReadModel(FlowId FlowId, string FlowName, string StepName);

public interface IRequestTemplateUsageQuery
{
    Task<IReadOnlyList<RequestTemplateUsageReadModel>> FindFlowsReferencing(RequestTemplateId templateId, TenantId tenantId, CancellationToken ct);
}

// DISC-20: the list a template-management page's own table needs — mirrors
// IProjectSummaryQuery's own shape (a read model, never served off
// IRequestTemplateRepository directly, FR-CQRS-01-style). Name/BaseUrl/Version
// only; a row doesn't need the full Headers/Auth payload just to be listed.
public sealed record RequestTemplateSummary(RequestTemplateId Id, string Name, string? BaseUrl, int Version);

public interface IRequestTemplateSummaryQuery
{
    Task<IReadOnlyList<RequestTemplateSummary>> ListByTenant(TenantId tenantId, CancellationToken ct);
}
