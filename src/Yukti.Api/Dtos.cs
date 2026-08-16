using Yukti.Application.Abstractions;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.ModulePlugin;

namespace Yukti.Api;

// Deliberately separate from the domain model — the API layer maps
// aggregates to these shapes rather than serializing Flow/FlowRun/FlowStep
// directly, so strongly-typed IDs, private setters, and invariants stay
// encapsulated inside the domain (Volume 1 Part V, API/Data layer boundary).

public sealed record CreateFlowRequest(string Name, string? Description, Guid? ProjectId = null);

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

public sealed record CreateApiCollectionRequest(string Name, string? Description, Guid? ProjectId = null);
public sealed record RenameApiCollectionRequest(string Name, string? Description);
public sealed record AddApiRequestRequest(
    string Name, string Method, string Url,
    Dictionary<string, object?>? Headers, Dictionary<string, object?>? QueryParams,
    object? Body, object? Assertions);
public sealed record UpdateApiRequestRequest(
    string Name, string Method, string Url,
    Dictionary<string, object?>? Headers, Dictionary<string, object?>? QueryParams,
    object? Body, object? Assertions);

public sealed record ApiRequestResponse(
    Guid Id, string Name, string Method, string Url,
    IReadOnlyDictionary<string, object?> Headers, IReadOnlyDictionary<string, object?> QueryParams,
    object? Body, object? Assertions, int Order)
{
    public static ApiRequestResponse From(ApiRequestSummary r) => new(
        r.Id.Value, r.Name, r.Method, r.Url, r.Headers, r.QueryParams, r.Body, r.Assertions, r.Order);
}

public sealed record ApiCollectionResponse(
    Guid Id, string Name, string? Description, IReadOnlyList<ApiRequestResponse> Requests, Guid? ProjectId)
{
    public static ApiCollectionResponse From(ApiCollectionSummary c) => new(
        c.Id.Value, c.Name, c.Description, c.Requests.Select(ApiRequestResponse.From).ToList(), c.ProjectId?.Value);
}

public sealed record CreateProjectRequest(string Name, string? Description);
public sealed record RenameProjectRequest(string Name, string? Description);

public sealed record ProjectResponse(Guid Id, string Name, string? Description)
{
    public static ProjectResponse From(ProjectSummary p) => new(p.Id.Value, p.Name, p.Description);
}

public sealed record CreateTestEnvironmentRequest(string Name, Dictionary<string, object?>? Variables);
public sealed record UpdateTestEnvironmentRequest(string Name, Dictionary<string, object?>? Variables);

public sealed record TestEnvironmentResponse(Guid Id, Guid ProjectId, string Name, IReadOnlyDictionary<string, object?> Variables)
{
    public static TestEnvironmentResponse From(TestEnvironmentSummary e) => new(e.Id.Value, e.ProjectId.Value, e.Name, e.Variables);
}

public sealed record CreateTriggerRequest(string Kind, string? CronExpression, string? WebhookSecret, string? WatchPath);

// WebhookSecret is deliberately never included — write-only, same
// redaction discipline FR-AUDIT-02 applies to sensitive command fields.
public sealed record TriggerResponse(
    Guid Id, Guid FlowId, string Kind, bool IsEnabled, DateTimeOffset? LastFiredAt,
    string? CronExpression, string? WebhookPath, string? WatchPath)
{
    public static TriggerResponse From(TriggerSummary t) => new(
        t.Id.Value, t.FlowId.Value, t.Kind.ToString(), t.IsEnabled, t.LastFiredAt, t.CronExpression, t.WebhookPath, t.WatchPath);
}

public sealed record AuditEntryResponse(Guid Id, string CommandType, Guid? TenantId, bool Succeeded, string? FailureReason, DateTimeOffset OccurredAt)
{
    public static AuditEntryResponse From(AuditEntrySummary a) => new(
        a.Id.Value, a.CommandType, a.TenantId?.Value, a.Succeeded, a.FailureReason, a.OccurredAt);
}

public sealed record AuditEntryDetailResponse(
    Guid Id, string CommandType, Guid? TenantId, bool Succeeded, string? FailureReason,
    IReadOnlyDictionary<string, object?> Metadata, DateTimeOffset OccurredAt)
{
    public static AuditEntryDetailResponse From(AuditEntryDetail a) => new(
        a.Id.Value, a.CommandType, a.TenantId?.Value, a.Succeeded, a.FailureReason, a.Metadata, a.OccurredAt);
}

public sealed record FlowReportSummaryResponse(
    Guid FlowId, string FlowName, int TotalRuns, int PassedRuns, int FailedRuns,
    DateTimeOffset LastRunAt, string LastRunStatus)
{
    public static FlowReportSummaryResponse From(FlowReportSummary s) => new(
        s.FlowId.Value, s.FlowName, s.TotalRuns, s.PassedRuns, s.FailedRuns, s.LastRunAt, s.LastRunStatus.ToString());
}

public sealed record FlowRunReportResponse(
    Guid FlowRunId, string FinalStatus, int PassedCount, int FailedCount, int SkippedCount,
    double TotalDurationMs, DateTimeOffset OccurredAt, DateTimeOffset ProjectedAt)
{
    public static FlowRunReportResponse From(FlowRunReportEntry e) => new(
        e.FlowRunId.Value, e.FinalStatus.ToString(), e.PassedCount, e.FailedCount, e.SkippedCount,
        e.TotalDuration.TotalMilliseconds, e.OccurredAt, e.ProjectedAt);
}

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
