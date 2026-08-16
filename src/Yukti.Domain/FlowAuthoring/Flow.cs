using Yukti.Domain.Events;
using Yukti.Domain.SharedKernel;

namespace Yukti.Domain.FlowAuthoring;

public sealed record FlowPublishResult(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static FlowPublishResult Success() => new(true, Array.Empty<string>());
    public static FlowPublishResult Failure(IEnumerable<string> errors) => new(false, errors.ToList());
}

/// <summary>
/// Aggregate root of the Flow Authoring context. Invariants:
///  - A Published Flow is immutable; editing requires a new version sharing
///    FamilyId with an incremented Version, never mutation in place. This is
///    what lets Execution trust a FlowRun referencing a specific version will
///    never see its steps change underneath it.
///  - A Flow may only transition Draft → Published if every FlowStep's
///    (ModuleKind, action) pair resolves against a currently-registered
///    ModuleRegistration, and every {{vars.x.y}} reference resolves against
///    a prior step's saveAs binding.
///  - FlowStep ordering is significant and part of the consistency boundary.
/// (Volume 1 Part II §9.2)
/// </summary>
public sealed class Flow : AggregateRoot<FlowId>
{
    public FlowFamilyId FamilyId { get; private set; }
    public int Version { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public FlowStatus Status { get; private set; }
    public bool ContinueOnFailure { get; private set; }
    public TenantId TenantId { get; private set; }
    public UserId CreatedBy { get; private set; }

    // Optional scoping to a ProjectManagement.Project — purely organizational
    // (filters what the FE lists/shows), never enforced as an authorization
    // boundary; TenantId remains the only real isolation guarantee.
    public ProjectId? ProjectId { get; private set; }

    private readonly List<FlowStep> _steps = new();
    public IReadOnlyList<FlowStep> Steps => _steps.AsReadOnly();

    private readonly Dictionary<string, object?> _declaredVariables;
    public IReadOnlyDictionary<string, object?> DeclaredVariables => _declaredVariables;

    private Flow(FlowId id, FlowFamilyId familyId, int version, string name, TenantId tenantId, UserId createdBy)
        : base(id)
    {
        FamilyId = familyId;
        Version = version;
        Name = name;
        Status = FlowStatus.Draft;
        TenantId = tenantId;
        CreatedBy = createdBy;
        _declaredVariables = new Dictionary<string, object?>();
    }

    public static Flow CreateDraft(string name, string? description, TenantId tenantId, UserId createdBy, ProjectId? projectId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Flow name cannot be empty.");

        var flow = new Flow(FlowId.New(), FlowFamilyId.New(), version: 1, name, tenantId, createdBy)
        {
            Description = description,
            ProjectId = projectId
        };
        return flow;
    }

    /// <summary>Creates a new Draft version sharing this flow's family — the only way to edit a Published flow.</summary>
    public Flow CreateNewVersion(UserId createdBy)
    {
        var next = new Flow(FlowId.New(), FamilyId, Version + 1, Name, TenantId, createdBy)
        {
            ProjectId = ProjectId,
            Description = Description
        };
        foreach (var step in _steps)
            next._steps.Add(step);
        return next;
    }

    public void AddStep(string name, ModulePlugin.ModuleKind module, string action,
        IReadOnlyDictionary<string, object?> parameters, string? saveAs = null, string? whenCondition = null)
    {
        if (Status != FlowStatus.Draft)
            throw new DomainException("Cannot modify a non-draft Flow. Create a new version instead.");

        var step = new FlowStep(FlowStepId.New(), name, module, action, parameters, _steps.Count, saveAs, whenCondition);
        _steps.Add(step);
    }

    public void SetContinueOnFailure(bool value)
    {
        if (Status != FlowStatus.Draft)
            throw new DomainException("Cannot modify a non-draft Flow. Create a new version instead.");
        ContinueOnFailure = value;
    }

    /// <summary>
    /// Validates every step's (ModuleKind, action) against the resolver and
    /// every variable reference against prior steps' saveAs bindings, then
    /// transitions Status to Published only if all resolve successfully.
    /// Returns a result rather than throwing — an unresolved module
    /// reference is an expected, reportable outcome, not exceptional
    /// program state (Architecture Principle 20.4).
    /// </summary>
    public FlowPublishResult Publish(IModuleActionResolver resolver)
    {
        if (Status != FlowStatus.Draft)
            return FlowPublishResult.Failure(new[] { $"Flow is already {Status}; only Draft flows can be published." });

        var errors = new List<string>();
        var availableBindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var step in _steps.OrderBy(s => s.Order))
        {
            if (!resolver.ActionExists(step.Module, step.Action))
                errors.Add($"Step '{step.Name}' references action '{step.Module}.{step.Action}' which is not registered.");

            foreach (var referencedPath in step.GetReferencedVariablePaths())
            {
                // Referenced paths look like "vars.orderInfo.id" — the bound name is the second segment.
                var segments = referencedPath.Split('.');
                if (segments.Length < 2 || segments[0] != "vars")
                    continue;
                var boundName = segments[1];
                if (!availableBindings.Contains(boundName))
                    errors.Add($"Step '{step.Name}' references undeclared variable '{boundName}'.");
            }

            if (step.SaveAs is not null)
                availableBindings.Add(step.SaveAs);
        }

        if (errors.Count > 0)
            return FlowPublishResult.Failure(errors);

        Status = FlowStatus.Published;
        RaiseDomainEvent(new FlowPublishedEvent(Id, FamilyId, Version, TenantId, CreatedBy, DateTimeOffset.UtcNow));
        return FlowPublishResult.Success();
    }

    public void Archive(UserId archivedBy)
    {
        if (Status != FlowStatus.Published)
            throw new DomainException("Only Published flows can be archived.");
        Status = FlowStatus.Archived;
        RaiseDomainEvent(new FlowArchivedEvent(Id, TenantId, archivedBy, DateTimeOffset.UtcNow));
    }
}
