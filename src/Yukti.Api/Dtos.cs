using Yukti.Application.Abstractions;
using Yukti.Domain.ApiTesting;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.ModulePlugin;

namespace Yukti.Api;

// Deliberately separate from the domain model — the API layer maps
// aggregates to these shapes rather than serializing Flow/FlowRun/FlowStep
// directly, so strongly-typed IDs, private setters, and invariants stay
// encapsulated inside the domain (Volume 1 Part V, API/Data layer boundary).

public sealed record CreateFlowRequest(string Name, string? Description, Guid? ProjectId = null);

/// <summary>D5-T7: plain-English description of the test to generate.</summary>
public sealed record GenerateFlowRequest(string Description);

/// <summary>D5-T9/§R10: week-to-date AI spend for the caller's own tenant. <paramref name="Warning"/> is true
/// once spend crosses the configured warn threshold (default 80%) of <paramref name="CapUsd"/>.
/// DISC-14: <paramref name="RequestsToday"/>/<paramref name="DailyRequestQuota"/> are global (not
/// tenant-scoped — the underlying constraint is a provider's own per-key request quota, shared
/// across every tenant and even across runtimes), unlike every other field here.</summary>
public sealed record AiUsageResponse(
    decimal TotalCostUsd, decimal CapUsd, double PercentOfCap, bool Warning,
    int TotalInputTokens, int TotalOutputTokens, int BudgetRefusals, double RetryShare,
    int RequestsToday, int DailyRequestQuota,
    IReadOnlyList<AiCapabilityUsageResponse> ByCapability);

public sealed record AiCapabilityUsageResponse(string Capability, int Calls, int Retries, int InputTokens, int OutputTokens, decimal CostUsd);

/// <summary>
/// D5-T7: <paramref name="Status"/> is <c>"generated"</c> (a Draft flow was saved — redirect to
/// <paramref name="FlowId"/>) or <c>"ambiguous"</c> (nothing was created; <paramref
/// name="Ambiguities"/> are Analyst's own reasons, for the UI's clarifying-question state).
/// <paramref name="Confidence"/> is the D5-T6 formula's value, never a literal.
/// </summary>
public sealed record GenerateFlowResponse(
    string Status, Guid? FlowId, string? Capability, double? Confidence, string? ModelIdentifier, IReadOnlyList<string> Ambiguities);

public sealed record AddStepRequest(
    string Name, string Module, string Action,
    Dictionary<string, object?> Params, string? SaveAs, string? When);

public sealed record TriggerRunRequest(Dictionary<string, object?>? VariableOverrides);

public sealed record FlowStepResponse(
    Guid Id, string Name, string Module, string Action,
    IReadOnlyDictionary<string, object?> Params, string? SaveAs, string? When, int Order,
    AiAttributionResponse? AiAttribution)
{
    public static FlowStepResponse From(FlowStep step) => new(
        step.Id.Value, step.Name, step.Module.Value, step.Action,
        step.Params, step.SaveAs, step.WhenCondition, step.Order,
        step.AiAttribution is { } a ? new AiAttributionResponse(a.Capability, a.Confidence, a.ModelIdentifier, a.PromptVersion) : null);
}

/// <summary>D5-T6/T7: null on every manually-authored step; present only on a Coder-generated one.</summary>
public sealed record AiAttributionResponse(string Capability, double Confidence, string ModelIdentifier, string PromptVersion);

/// <summary>D5-T7 (closing D5-T2's deferred "UI hides the generation panel"): lets the UI know up
/// front, rather than discovering AI is off only by submitting and getting a 503.</summary>
public sealed record AiStatusResponse(bool Enabled);

public sealed record FlowResponse(
    Guid Id, Guid FamilyId, int Version, string Name, string? Description,
    string Status, bool ContinueOnFailure, IReadOnlyList<FlowStepResponse> Steps)
{
    public static FlowResponse From(Flow flow) => new(
        flow.Id.Value, flow.FamilyId.Value, flow.Version, flow.Name, flow.Description,
        flow.Status.ToString(), flow.ContinueOnFailure, flow.Steps.Select(FlowStepResponse.From).ToList());
}

public sealed record PublishErrorResponse(string Field, string Code, string Message)
{
    public static PublishErrorResponse From(FlowPublishError error) => new(error.Field, error.Code, error.Message);
}

public sealed record PublishResponse(bool Succeeded, IReadOnlyList<PublishErrorResponse> Errors)
{
    public static PublishResponse From(FlowPublishResult result) => new(result.Succeeded, result.Errors.Select(PublishErrorResponse.From).ToList());
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

public sealed record CreateRequestTemplateRequest(string Name, string? BaseUrl, Dictionary<string, object?>? Headers, object? Auth, int? DefaultTimeoutMs);
public sealed record UpdateRequestTemplateRequest(string Name, string? BaseUrl, Dictionary<string, object?>? Headers, object? Auth, int? DefaultTimeoutMs);
public sealed record CreatedRequestTemplateResponse(Guid TemplateId);
public sealed record RequestTemplateResponse(Guid Id, string Name, string? BaseUrl, IReadOnlyDictionary<string, object?> Headers, object? Auth, int? DefaultTimeoutMs, int Version)
{
    public static RequestTemplateResponse From(RequestTemplate t) => new(t.Id.Value, t.Name, t.BaseUrl, t.Headers, t.Auth, t.DefaultTimeoutMs, t.Version);
}
public sealed record RequestTemplateUsageResponse(Guid FlowId, string FlowName, string StepName)
{
    public static RequestTemplateUsageResponse From(RequestTemplateUsageReadModel u) => new(u.FlowId.Value, u.FlowName, u.StepName);
}
public sealed record RequestTemplateSummaryResponse(Guid Id, string Name, string? BaseUrl, int Version)
{
    public static RequestTemplateSummaryResponse From(RequestTemplateSummary s) => new(s.Id.Value, s.Name, s.BaseUrl, s.Version);
}

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

public sealed record LocatorResolutionResponse(string StrategyUsed, string PrimaryFailureReason, string? Description)
{
    public static LocatorResolutionResponse From(Yukti.Domain.Execution.LocatorResolutionInfo info) =>
        new(info.StrategyUsed.ToString(), info.PrimaryFailureReason, info.Description);
}

public sealed record StepResultResponse(
    Guid Id, string StepName, string Module, string Action, string Status,
    double DurationMs, string? Message, string? Error, object? Data, bool IsFlaky,
    IReadOnlyList<RetryAttemptResponse> RetryHistory, bool UsedLocatorFallback, LocatorResolutionResponse? LocatorResolution)
{
    // D6-T3a: a module's Data can carry the same shape a header can — ApiModule's own response
    // "headers" field, which can include a server's Set-Cookie — so the run API response needs
    // the identical redaction the report writers apply, not a separate copy of the same logic.
    public static StepResultResponse From(StepResult result) => new(
        result.Id.Value, result.StepName, result.Module.Value, result.Action, result.Status.ToString(),
        result.Duration.TotalMilliseconds, result.Message, result.Error,
        result.Data is IReadOnlyDictionary<string, object?> dict ? Yukti.Domain.Auditing.SecretRedactor.Redact(dict) : result.Data,
        result.IsFlaky,
        result.RetryHistory.Select(a => new RetryAttemptResponse(
            a.AttemptNumber, a.Status.ToString(), a.Duration.TotalMilliseconds, a.Error)).ToList(),
        result.UsedLocatorFallback,
        result.LocatorResolution is null ? null : LocatorResolutionResponse.From(result.LocatorResolution));
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

// DISC-3: named response records for what used to be anonymous-typed
// `Results.Created(uri, new { ... })` bodies. Swagger/openapi-typescript
// can't generate a real response type for an anonymous object — these give
// it one, with the same property names (and therefore the same JSON shape
// under the default camelCase policy) as what they replace.

public sealed record CreatedUserResponse(Guid UserId);
public sealed record RolePermissionsUpdatedResponse(Guid RoleId, int Version);
public sealed record CreatedFlowResponse(Guid FlowId);
public sealed record CreatedApiCollectionResponse(Guid CollectionId);
public sealed record CreatedApiRequestResponse(Guid RequestId);
public sealed record CreatedProjectResponse(Guid ProjectId);
public sealed record CreatedTestEnvironmentResponse(Guid EnvironmentId);
public sealed record CreatedTriggerResponse(Guid TriggerId);
public sealed record WebhookTriggeredResponse(Guid RunId, string Status);
public sealed record RootStatusResponse(string Service, string Status);
public sealed record HealthResponse(string Status);
