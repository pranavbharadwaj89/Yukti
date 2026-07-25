namespace Yukti.Domain.SharedKernel;

// Strongly-typed identifiers, per Volume 1 Part II §10.8 / §11.10.
// Every aggregate/entity identifier is a distinct type, never a raw Guid,
// so a method call with transposed arguments fails to compile rather than
// failing silently at runtime.

public readonly record struct FlowId(Guid Value)
{
    public static FlowId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public readonly record struct FlowFamilyId(Guid Value)
{
    public static FlowFamilyId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public readonly record struct FlowStepId(Guid Value)
{
    public static FlowStepId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public readonly record struct FlowRunId(Guid Value)
{
    public static FlowRunId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public readonly record struct StepResultId(Guid Value)
{
    public static StepResultId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public readonly record struct RetryAttemptId(Guid Value)
{
    public static RetryAttemptId New() => new(Guid.NewGuid());
}

public readonly record struct ModuleRegistrationId(Guid Value)
{
    public static ModuleRegistrationId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public readonly record struct ModuleActionEntryId(Guid Value)
{
    public static ModuleActionEntryId New() => new(Guid.NewGuid());
}

public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public readonly record struct TenantId(Guid Value)
{
    public static TenantId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public readonly record struct RoleId(Guid Value)
{
    public static RoleId New() => new(Guid.NewGuid());
}

public readonly record struct AuditEntryId(Guid Value)
{
    public static AuditEntryId New() => new(Guid.NewGuid());
}
