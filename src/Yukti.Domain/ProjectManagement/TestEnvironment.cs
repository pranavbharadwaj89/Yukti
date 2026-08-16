using Yukti.Domain.SharedKernel;

namespace Yukti.Domain.ProjectManagement;

/// <summary>
/// A named, reusable set of variables scoped to a Project — a saved
/// TriggerFlowRunCommand.VariableOverrides bag (Yukti.Application.Execution.
/// Commands), nothing more. Deliberately no dedicated "Mobile device config"
/// shape: MobileModule.Setup already reads device config from
/// ctx.Variables["mobile"] (Yukti.Infrastructure.InMemory.Modules.
/// MobileModule.cs), so storing it under that same reserved key here means
/// the engine needs zero changes to consume a TestEnvironment's Variables —
/// the FE just passes them straight through as variableOverrides.
/// </summary>
public sealed class TestEnvironment : AggregateRoot<TestEnvironmentId>
{
    public ProjectId ProjectId { get; private set; }
    public TenantId TenantId { get; private set; }
    public string Name { get; private set; }
    public IReadOnlyDictionary<string, object?> Variables { get; private set; }

    private TestEnvironment(
        TestEnvironmentId id, ProjectId projectId, string name,
        IReadOnlyDictionary<string, object?> variables, TenantId tenantId) : base(id)
    {
        ProjectId = projectId;
        Name = name;
        Variables = variables;
        TenantId = tenantId;
    }

    public static TestEnvironment Create(
        string name, ProjectId projectId, IReadOnlyDictionary<string, object?> variables, TenantId tenantId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("TestEnvironment name cannot be empty.");
        return new TestEnvironment(TestEnvironmentId.New(), projectId, name, variables, tenantId);
    }

    public void Update(string name, IReadOnlyDictionary<string, object?> variables)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("TestEnvironment name cannot be empty.");
        Name = name;
        Variables = variables;
    }
}
