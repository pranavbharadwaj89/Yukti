using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.ModulePlugin;

namespace Yukti.Api;

// Deliberately separate from the domain model — the API layer maps
// aggregates to these shapes rather than serializing Flow/FlowRun/FlowStep
// directly, so strongly-typed IDs, private setters, and invariants stay
// encapsulated inside the domain (Volume 1 Part V, API/Data layer boundary).

public sealed record CreateFlowRequest(string Name, string? Description);

public sealed record AddStepRequest(
    string Name, string Module, string Action,
    Dictionary<string, object?> Params, string? SaveAs, string? When);

public sealed record TriggerRunRequest(Dictionary<string, object?>? VariableOverrides);

public sealed record FlowStepResponse(
    Guid Id, string Name, string Module, string Action,
    IReadOnlyDictionary<string, object?> Params, string? SaveAs, string? When, int Order)
{
    public static FlowStepResponse From(FlowStep step) => new(
        step.Id.Value, step.Name, step.Module.Value, step.Action,
        step.Params, step.SaveAs, step.WhenCondition, step.Order);
}

public sealed record FlowResponse(
    Guid Id, Guid FamilyId, int Version, string Name, string? Description,
    string Status, bool ContinueOnFailure, IReadOnlyList<FlowStepResponse> Steps)
{
    public static FlowResponse From(Flow flow) => new(
        flow.Id.Value, flow.FamilyId.Value, flow.Version, flow.Name, flow.Description,
        flow.Status.ToString(), flow.ContinueOnFailure, flow.Steps.Select(FlowStepResponse.From).ToList());
}

public sealed record PublishResponse(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static PublishResponse From(FlowPublishResult result) => new(result.Succeeded, result.Errors);
}

public sealed record RetryAttemptResponse(int AttemptNumber, string Status, double DurationMs, string? Error);

public sealed record StepResultResponse(
    Guid Id, string StepName, string Module, string Action, string Status,
    double DurationMs, string? Message, string? Error, object? Data, bool IsFlaky,
    IReadOnlyList<RetryAttemptResponse> RetryHistory)
{
    public static StepResultResponse From(StepResult result) => new(
        result.Id.Value, result.StepName, result.Module.Value, result.Action, result.Status.ToString(),
        result.Duration.TotalMilliseconds, result.Message, result.Error, result.Data, result.IsFlaky,
        result.RetryHistory.Select(a => new RetryAttemptResponse(
            a.AttemptNumber, a.Status.ToString(), a.Duration.TotalMilliseconds, a.Error)).ToList());
}

public sealed record FlowRunResponse(
    Guid Id, Guid FlowId, string Status, string Trigger,
    DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, IReadOnlyList<StepResultResponse> Results)
{
    public static FlowRunResponse From(FlowRun run) => new(
        run.Id.Value, run.FlowId.Value, run.Status.ToString(), run.Trigger.ToString(),
        run.StartedAt, run.FinishedAt, run.Results.Select(StepResultResponse.From).ToList());
}

public sealed record ActionParamResponse(string Name, string Type, bool Required, object? DefaultValue, string? Description)
{
    public static ActionParamResponse From(ParamSpec spec) => new(
        spec.Name, spec.Type.ToString(), spec.Required, spec.DefaultValue, spec.Description);
}

public sealed record ActionSchemaResponse(string ActionName, string? Description, IReadOnlyList<ActionParamResponse> Parameters)
{
    public static ActionSchemaResponse From(ActionSchema schema) => new(
        schema.ActionName, schema.Description, schema.Parameters.Select(ActionParamResponse.From).ToList());
}

/// <summary>Backs FR-ORCH-7's introspection requirement — a flow-authoring GUI renders purely from this, with zero hardcoded per-module knowledge.</summary>
public sealed record ModuleResponse(string Kind, string DisplayName, string Trust, string ContractVersion, IReadOnlyList<ActionSchemaResponse> Actions);

// ---- Auth (Volume 1 Part IV §24-25) ----

public sealed record RegisterRequest(string Email, string Password, string DisplayName);
public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record UpdateRolePermissionsRequest(IReadOnlyList<string> Permissions);

public sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAt, string RefreshToken);
