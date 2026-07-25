using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;

namespace Yukti.Domain.Execution;

/// <summary>
/// StepName/Module/Action are denormalized onto StepResult rather than
/// always joined from FlowStep because FlowSteps can be edited in a Draft
/// between when a historical run occurred and when a report is viewed —
/// FlowRun pins to an immutable Flow *version*, and denormalizing guarantees
/// a report always reflects exactly what ran, not what the step currently
/// looks like in a possibly-newer version. (Volume 1 Part II §10.3, §10.3.1)
/// </summary>
public sealed class StepResult : Entity<StepResultId>
{
    public FlowStepId SourceStepId { get; }
    public string StepName { get; }
    public ModuleKind Module { get; }
    public string Action { get; }
    public StepStatus Status { get; }
    public TimeSpan Duration { get; }
    public string? Message { get; }
    public string? Error { get; }
    public object? Data { get; }
    public AiAttribution? AiAttribution { get; }

    private readonly List<RetryAttempt> _retryHistory;
    public IReadOnlyList<RetryAttempt> RetryHistory => _retryHistory.AsReadOnly();

    public StepResult(
        FlowStepId sourceStepId, string stepName, ModuleKind module, string action,
        StepStatus status, TimeSpan duration,
        string? message = null, string? error = null, object? data = null,
        AiAttribution? aiAttribution = null, IEnumerable<RetryAttempt>? retryHistory = null)
        : base(StepResultId.New())
    {
        SourceStepId = sourceStepId;
        StepName = stepName;
        Module = module;
        Action = action;
        Status = status;
        Duration = duration;
        Message = message;
        Error = error;
        Data = data;
        AiAttribution = aiAttribution;
        _retryHistory = retryHistory?.ToList() ?? new List<RetryAttempt>();
    }

    public static StepResult Skipped(FlowStepId sourceStepId, string stepName, ModuleKind module, string action, string reason) =>
        new(sourceStepId, stepName, module, action, StepStatus.Skipped, TimeSpan.Zero, message: reason);

    public bool IsFlaky => Status == StepStatus.Passed && _retryHistory.Count > 0;
}
