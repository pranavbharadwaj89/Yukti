using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;

namespace Yukti.Domain.FlowAuthoring;

/// <summary>Lifecycle state of a Flow. (Volume 1 Part II §11.4)</summary>
public enum FlowStatus { Draft, Published, Archived }

/// <summary>
/// Identity stable across edits to the step's own parameters within a
/// Draft; never mutated after the parent Flow is Published (Flow's
/// immutability invariant, §9.2). Params is deliberately weakly-typed —
/// FlowStep cannot know a module's expected parameter shape at compile
/// time; that shape is defined by ActionSchema, resolved only at
/// validation/execution time. (Volume 1 Part II §10.2)
/// </summary>
public sealed class FlowStep : Entity<FlowStepId>
{
    public string Name { get; private set; }
    public ModuleKind Module { get; private set; }
    public string Action { get; private set; }
    public IReadOnlyDictionary<string, object?> Params { get; private set; }
    public string? SaveAs { get; private set; }
    public string? WhenCondition { get; private set; }
    public int Order { get; private set; }

    public FlowStep(
        FlowStepId id,
        string name,
        ModuleKind module,
        string action,
        IReadOnlyDictionary<string, object?> parameters,
        int order,
        string? saveAs = null,
        string? whenCondition = null) : base(id)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("FlowStep name cannot be empty.");
        if (string.IsNullOrWhiteSpace(action))
            throw new DomainException("FlowStep action cannot be empty.");

        Name = name;
        Module = module;
        Action = action;
        Params = parameters;
        Order = order;
        SaveAs = saveAs;
        WhenCondition = whenCondition;
    }

    /// <summary>Every {{vars.x.y}} reference across every param value, for publish-time validation.</summary>
    public IEnumerable<string> GetReferencedVariablePaths()
    {
        foreach (var value in Params.Values)
        {
            if (value is string s && VariableExpression.TryParse(s, out var expr))
                foreach (var path in expr!.ReferencedPaths)
                    yield return path;
        }
        if (WhenCondition is not null && VariableExpression.TryParse(WhenCondition, out var whenExpr))
            foreach (var path in whenExpr!.ReferencedPaths)
                yield return path;
    }
}
