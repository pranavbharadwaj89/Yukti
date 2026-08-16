using Yukti.Domain.SharedKernel;

namespace Yukti.Domain.ProjectManagement;

/// <summary>
/// A named grouping of Flows and ApiCollections within a tenant — lets a
/// user organize unrelated work ("Project A", "Project B") and scope what
/// the FE shows/runs against. Deliberately as lightweight as ApiCollection
/// (Yukti.Domain.ApiTesting.ApiCollection): no nested entities, no publish
/// step, just a named container other aggregates optionally reference by
/// ProjectId.
/// </summary>
public sealed class Project : AggregateRoot<ProjectId>
{
    public TenantId TenantId { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }

    private Project(ProjectId id, string name, string? description, TenantId tenantId) : base(id)
    {
        Name = name;
        Description = description;
        TenantId = tenantId;
    }

    public static Project Create(string name, string? description, TenantId tenantId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Project name cannot be empty.");
        return new Project(ProjectId.New(), name, description, tenantId);
    }

    public void Rename(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Project name cannot be empty.");
        Name = name;
        Description = description;
    }
}
