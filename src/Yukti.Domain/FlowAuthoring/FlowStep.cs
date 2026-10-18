using Yukti.Domain.Execution;
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

    /// <summary>
    /// D5-T6 (CLAUDE.md principle 3): null for every manually/CLI-authored step — the ordinary
    /// case, and the only case until Sarvagna's planning pipeline (D5-T3/T5) existed. Non-null
    /// only for a step the Coder capability actually generated, carrying the real capability,
    /// model, prompt version, and a confidence computed from the planning engine's own retry
    /// outcome (<see cref="Yukti.Application.Ai.AiConfidence"/>) — never a literal, never asked
    /// of the model.
    /// </summary>
    public AiAttribution? AiAttribution { get; private set; }

    // "parameters" renamed to "params" so EF Core's constructor binding
    // (parameter-name-to-property-name, case-insensitive) can materialize
    // this entity from its mapped Params column — Flow.AddStep calls this
    // constructor positionally, so the rename has zero effect there.
    public FlowStep(
        FlowStepId id,
        string name,
        ModuleKind module,
        string action,
        IReadOnlyDictionary<string, object?> @params,
        int order,
        string? saveAs = null,
        string? whenCondition = null,
        AiAttribution? aiAttribution = null) : base(id)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("FlowStep name cannot be empty.");
        if (string.IsNullOrWhiteSpace(action))
            throw new DomainException("FlowStep action cannot be empty.");

        Name = name;
        Module = module;
        Action = action;
        Params = @params;
        Order = order;
        SaveAs = saveAs;
        WhenCondition = whenCondition;
        AiAttribution = aiAttribution;
    }

    /// <summary>
    /// Every {{vars.x.y}} reference across every param value plus the when
    /// condition, each paired with the field it came from — "params.name"
    /// or "when" — so Flow.Publish can report a structured
    /// steps[i].params.&lt;name&gt; error instead of just naming the step
    /// (D2-T5). Only scans top-level string param values, matching this
    /// method's pre-existing scope — a template nested inside an
    /// object/array param isn't caught here (nor was it before); that's a
    /// pre-existing gap in publish-time validation, not something D2-T5's
    /// error-shape change introduces or fixes.
    /// </summary>
    public IEnumerable<(string Field, string Path)> GetReferencedVariableReferences()
    {
        foreach (var (name, value) in Params)
        {
            if (value is string s && VariableExpression.TryParse(s, out var expr))
                foreach (var path in expr!.ReferencedPaths)
                    yield return ($"params.{name}", path);
        }
        if (WhenCondition is not null && VariableExpression.TryParse(WhenCondition, out var whenExpr))
            foreach (var path in whenExpr!.ReferencedPaths)
                yield return ("when", path);
    }
}
