namespace Yukti.Domain.ModulePlugin;

public enum ParamType { String, Number, Boolean, Object, Array }

public sealed record ParamSpec
{
    public required string Name { get; init; }
    public required ParamType Type { get; init; }
    public bool Required { get; init; }
    public object? DefaultValue { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// The shared contract both the Flow Parser (validating a FlowStep's Params
/// at publish time) and the Module Registration API (exposing available
/// actions to the GUI, per FR-ORCH-7) depend on. (Volume 1 Part II §11.8)
/// </summary>
public sealed record ActionSchema
{
    public required string ActionName { get; init; }
    public required IReadOnlyList<ParamSpec> Parameters { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// TrustTier — maps directly to Volume 0's marketplace certification tiers,
/// now a first-class part of the runtime domain model. (Volume 1 Part II §11.9)
/// </summary>
public enum TrustTier { BuiltIn, Verified, Community }
