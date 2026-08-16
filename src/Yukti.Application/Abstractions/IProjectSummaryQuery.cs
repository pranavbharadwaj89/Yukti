using Yukti.Domain.SharedKernel;

namespace Yukti.Application.Abstractions;

// FR-CQRS-01-style read models, mirroring IApiCollectionSummaryQuery — list
// operations live here, never on IProjectRepository/ITestEnvironmentRepository.

public sealed record ProjectSummary(ProjectId Id, string Name, string? Description);

public interface IProjectSummaryQuery
{
    Task<IReadOnlyList<ProjectSummary>> ListByTenant(TenantId tenantId, CancellationToken ct);
}

public sealed record TestEnvironmentSummary(
    TestEnvironmentId Id, ProjectId ProjectId, string Name, IReadOnlyDictionary<string, object?> Variables);

public interface ITestEnvironmentSummaryQuery
{
    Task<IReadOnlyList<TestEnvironmentSummary>> ListByProject(ProjectId projectId, CancellationToken ct);
}
