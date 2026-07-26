using Microsoft.Extensions.Logging;
using Yukti.Application.Abstractions;
using Yukti.Contracts;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Orchestration;

/// <summary>
/// A direct, formalized evolution of the Orchestrator.runFlow logic
/// validated in this project's early prototype — fail-fast by default,
/// saveAs/when semantics, skip-vs-fail distinction all carry forward
/// unchanged, now expressed against the full domain model.
///
/// Incremental per-step commits (Volume 1 Part III §16.7/§19.2): every
/// step's result is committed via its own IUnitOfWork before the next step
/// dispatches. A process crash at any point after that line has run has
/// already durably recorded every step up to and including that one — the
/// specific, load-bearing guarantee this project corrected mid-way through
/// writing the architecture (originally the design batched all commits to
/// the run's end; that gap is what this loop's structure now closes).
///
/// Retry policy is applied uniformly per this session's scope — full
/// per-step-configurable retry policy is a follow-up pass.
/// (Volume 1 Part III §19.2)
/// </summary>
public sealed class FlowEngine
{
    private readonly IModuleDispatcher _dispatcher;
    private readonly IVariableStore _variables;
    private readonly IRetryFlakeHandler _retryHandler;
    private readonly IFlowRunRepository _runRepository;
    private readonly IUnitOfWorkFactory _uowFactory;
    private readonly RetryPolicy _defaultRetryPolicy;
    private readonly ILogger<FlowEngine> _logger;

    public FlowEngine(
        IModuleDispatcher dispatcher, IVariableStore variables, IRetryFlakeHandler retryHandler,
        IFlowRunRepository runRepository, IUnitOfWorkFactory uowFactory, ILogger<FlowEngine> logger,
        RetryPolicy? defaultRetryPolicy = null)
    {
        _dispatcher = dispatcher;
        _variables = variables;
        _retryHandler = retryHandler;
        _runRepository = runRepository;
        _uowFactory = uowFactory;
        _logger = logger;
        _defaultRetryPolicy = defaultRetryPolicy ?? new RetryPolicy(MaxAttempts: 2, InitialBackoff: TimeSpan.FromMilliseconds(200), BackoffMultiplier: 2.0);
    }

    public async Task<FlowRun> Execute(Flow flow, FlowRun run, ICredentialResolver credentials, CancellationToken ct)
    {
        // FR-LOG-03: every log statement inside this scope — including ones
        // raised deeper in _dispatcher/_retryHandler — carries FlowRunId
        // automatically. No call site below threads it through by hand.
        using var flowRunScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["FlowRunId"] = run.Id.Value
        });

        _logger.LogInformation("FlowRun {FlowRunId} starting for flow {FlowId} ({StepCount} steps)",
            run.Id.Value, flow.Id.Value, flow.Steps.Count);

        run.Start();
        await CommitRun(run, ct);

        foreach (var step in flow.Steps.OrderBy(s => s.Order))
        {
            ct.ThrowIfCancellationRequested();

            if (!_variables.EvaluateCondition(step.WhenCondition, run.Variables))
            {
                _logger.LogInformation("Step {StepName} skipped — condition {WhenCondition} was falsy",
                    step.Name, step.WhenCondition);
                run.RecordStepResult(StepResult.Skipped(step.Id, step.Name, step.Module, step.Action,
                    $"Condition '{step.WhenCondition}' was falsy."));
                await CommitRun(run, ct);
                continue;
            }

            var interpolatedParams = _variables.Interpolate(step.Params, run.Variables);
            var execCtx = new ExecutionContext
            {
                RunId = run.Id,
                Variables = run.Variables,
                Credentials = credentials,
                RunCancellation = ct
            };

            var retryOutcome = await _retryHandler.ExecuteWithRetry(
                innerCt => _dispatcher.Dispatch(step.Module, step.Action, interpolatedParams, execCtx, innerCt),
                _defaultRetryPolicy, ct);

            var result = new StepResult(
                step.Id, step.Name, step.Module, step.Action,
                retryOutcome.FinalOutcome.Status, TimeSpan.Zero,
                retryOutcome.FinalOutcome.Message, retryOutcome.FinalOutcome.Error, retryOutcome.FinalOutcome.Data,
                retryOutcome.FinalOutcome.AiAttribution, retryOutcome.Attempts);

            run.RecordStepResult(result);

            // FR-LOG-02: Error only for a genuine failure; a step that
            // eventually passed after retries is a flake — Warning, not
            // Error — surfaced by RetryFlakeHandler itself. A clean pass is
            // routine and logs at Information.
            if (result.Status == StepStatus.Failed)
                _logger.LogError("Step {StepName} ({Module}.{Action}) failed: {Error}",
                    step.Name, step.Module, step.Action, result.Error);
            else
                _logger.LogInformation("Step {StepName} ({Module}.{Action}) completed with status {Status}",
                    step.Name, step.Module, step.Action, result.Status);

            if (step.SaveAs is not null)
                run.BindVariable(step.SaveAs, result.Data);

            // Commit THIS step's result now, before dispatching the next step.
            await CommitRun(run, ct);

            if (result.Status == StepStatus.Failed && !flow.ContinueOnFailure)
            {
                _logger.LogWarning("FlowRun {FlowRunId} stopping early — step {StepName} failed and ContinueOnFailure is false",
                    run.Id.Value, step.Name);
                break; // fail-fast default (Product Philosophy 4.2)
            }
        }

        run.Complete();
        await CommitRun(run, ct);

        _logger.LogInformation("FlowRun {FlowRunId} finished with status {Status}", run.Id.Value, run.Status);
        return run;
    }

    private async Task CommitRun(FlowRun run, CancellationToken ct)
    {
        await _runRepository.Save(run, ct);
        await using var uow = _uowFactory.Create();
        await uow.Commit(ct);
    }
}
