using Yukti.Domain.Events;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.SharedKernel;

namespace Yukti.Domain.Execution;

/// <summary>
/// Aggregate root of the Execution context. Invariants:
///  - References an immutable Flow version by FlowId (never FlowFamilyId) —
///    always pinned to the exact version published at trigger time.
///  - StepResults are appended in step order and immutable once created.
///  - Once Status reaches a terminal state, no further StepResults may be
///    appended.
///
/// Per the incremental-commit design (Volume 1 Part III §16.7/§19.2): a
/// crash mid-run must lose at most the one step in flight, never the whole
/// run's history. This aggregate's Start()/RecordStepResult()/Complete()
/// are each designed to be independently committed by the orchestration
/// engine's per-step Unit of Work — not to be called all within one
/// transaction. (Volume 1 Part II §9.3)
/// </summary>
public sealed class FlowRun : AggregateRoot<FlowRunId>
{
    public FlowId FlowId { get; }
    public RunStatus Status { get; private set; }
    public RunTrigger Trigger { get; }
    public TenantId TenantId { get; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    private readonly Dictionary<string, object?> _variables;
    public IReadOnlyDictionary<string, object?> Variables => _variables;

    private readonly List<StepResult> _results = new();
    public IReadOnlyList<StepResult> Results => _results.AsReadOnly();

    private static readonly RunStatus[] TerminalStatuses = { RunStatus.Passed, RunStatus.Failed, RunStatus.Cancelled };

    // Parameter named to match the Variables property exactly (rather than
    // the public factory's "initialVariables") so EF Core's constructor
    // binding — which matches by parameter name, case-insensitively — can
    // materialize this aggregate straight from a persisted row. Purely a
    // private-constructor rename; FlowRun.Create's public signature below
    // is unaffected.
    private FlowRun(FlowRunId id, FlowId flowId, RunTrigger trigger, TenantId tenantId,
        IReadOnlyDictionary<string, object?>? variables) : base(id)
    {
        FlowId = flowId;
        Trigger = trigger;
        TenantId = tenantId;
        Status = RunStatus.Pending;
        _variables = variables is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(variables);
    }

    public static FlowRun Create(FlowId flowId, RunTrigger trigger, TenantId tenantId,
        IReadOnlyDictionary<string, object?>? initialVariables = null) =>
        new(FlowRunId.New(), flowId, trigger, tenantId, initialVariables);

    /// <summary>
    /// Transitions Pending → Running. Committed immediately, before the
    /// first step even dispatches, so a run that crashes before step 1
    /// completes is still durably visible as "started," never silently
    /// absent (Volume 1 Part III §16.7).
    /// </summary>
    public void Start()
    {
        if (Status != RunStatus.Pending)
            throw new DomainException($"Cannot start a FlowRun in status {Status}.");
        Status = RunStatus.Running;
        StartedAt = DateTimeOffset.UtcNow;
        RaiseDomainEvent(new FlowRunStartedEvent(Id, FlowId, Trigger, TenantId, StartedAt));
    }

    public void RecordStepResult(StepResult result)
    {
        if (IsTerminal(Status))
            throw new DomainException("Cannot record a step result on a completed FlowRun.");

        _results.Add(result);
        RaiseDomainEvent(new StepCompletedEvent(Id, result, DateTimeOffset.UtcNow));

        if (result.AiAttribution is not null)
            RaiseDomainEvent(new StepSelfHealedEvent(Id, result.Id, result.AiAttribution, DateTimeOffset.UtcNow));

        if (result.IsFlaky)
            RaiseDomainEvent(new FlowRunFlakeDetectedEvent(Id, result.Id, result.RetryHistory.Count, DateTimeOffset.UtcNow));
    }

    /// <summary>Binds a step's saveAs output into this run's variable scope for later steps' interpolation.</summary>
    public void BindVariable(string name, object? value) => _variables[name] = value;

    public void Complete()
    {
        if (IsTerminal(Status))
            throw new DomainException($"FlowRun is already in terminal status {Status}.");

        Status = _results.Any(r => r.Status == StepStatus.Failed) ? RunStatus.Failed : RunStatus.Passed;
        FinishedAt = DateTimeOffset.UtcNow;
        RaiseDomainEvent(new FlowRunCompletedEvent(
            Id, Status,
            PassedCount: _results.Count(r => r.Status == StepStatus.Passed),
            FailedCount: _results.Count(r => r.Status == StepStatus.Failed),
            SkippedCount: _results.Count(r => r.Status == StepStatus.Skipped),
            TotalDuration: FinishedAt.Value - StartedAt,
            OccurredAt: FinishedAt.Value));
    }

    public void Cancel()
    {
        if (IsTerminal(Status))
            throw new DomainException($"Cannot cancel a FlowRun already in terminal status {Status}.");
        Status = RunStatus.Cancelled;
        FinishedAt = DateTimeOffset.UtcNow;
    }

    private static bool IsTerminal(RunStatus status) => TerminalStatuses.Contains(status);
}
