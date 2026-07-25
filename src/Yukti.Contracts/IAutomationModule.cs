using Yukti.Domain.Execution;
using Yukti.Domain.ModulePlugin;

namespace Yukti.Contracts;

public sealed record StepOutcome
{
    public required StepStatus Status { get; init; }
    public string? Message { get; init; }
    public string? Error { get; init; }
    public object? Data { get; init; }
    public AiAttribution? AiAttribution { get; init; }

    public static StepOutcome Passed(string? message = null, object? data = null) =>
        new() { Status = StepStatus.Passed, Message = message, Data = data };

    public static StepOutcome Failed(string error, object? data = null) =>
        new() { Status = StepStatus.Failed, Error = error, Data = data };
}

/// <summary>
/// Every module — whether one of the six built-in modules or a third
/// party's custom module — implements this exact interface, and is
/// executed identically by the Flow Engine, reported on identically in
/// the unified report, and surfaced identically in the GUI's
/// flow-authoring interface. This is the concrete, auditable contract
/// that makes the Module Parity Index a testable property of the code,
/// not just a product goal. (Volume 1 Part III §18.2)
/// </summary>
public interface IAutomationModule
{
    ModuleKind Kind { get; }
    string ContractVersion { get; }

    /// <summary>Backs FR-ORCH-7's introspection — the GUI's flow-authoring surface renders
    /// purely from this, with zero hardcoded knowledge of any specific module.</summary>
    IReadOnlyList<ActionSchema> GetSupportedActions();

    Task Setup(ExecutionContext ctx, CancellationToken ct);

    Task<StepOutcome> Run(string action, IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct);

    Task Teardown(ExecutionContext ctx, CancellationToken ct);
}
