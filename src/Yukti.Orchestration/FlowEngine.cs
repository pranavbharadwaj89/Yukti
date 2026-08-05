using System.Diagnostics;
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
    private readonly IModuleRegistry _registry;
    private readonly IVariableStore _variables;
    private readonly IRetryFlakeHandler _retryHandler;
    private readonly IFlowRunRepository _runRepository;
    private readonly IUnitOfWorkFactory _uowFactory;
    private readonly RetryPolicy _defaultRetryPolicy;
    private readonly ILogger<FlowEngine> _logger;

    public FlowEngine(
        IModuleDispatcher dispatcher, IModuleRegistry registry, IVariableStore variables, IRetryFlakeHandler retryHandler,
        IFlowRunRepository runRepository, IUnitOfWorkFactory uowFactory, ILogger<FlowEngine> logger,
        RetryPolicy? defaultRetryPolicy = null)
    {
        _dispatcher = dispatcher;
        _registry = registry;
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

        // FR-OBS-01: one root trace per FlowRun — every step below opens its
        // own child Activity (System.Diagnostics.Activity automatically
        // parents a new Activity to whichever one is Activity.Current, which
        // this one becomes for the duration of the using block).
        using var runActivity = OrchestrationTelemetry.ActivitySource.StartActivity(
            "FlowRun.Execute", ActivityKind.Internal);
        runActivity?.SetTag("flow.run.id", run.Id.Value);
        runActivity?.SetTag("flow.id", flow.Id.Value);
        runActivity?.SetTag("flow.tenant.id", run.TenantId.Value);

        OrchestrationTelemetry.OrchestrationConcurrentExecutions.Add(1);

        // Only the modules this flow's steps actually reference get set up
        // — a flow that never uses `web` must not pay Chromium's
        // launch/close cost. Setup/Teardown were declared on
        // IAutomationModule from the start but nothing previously called
        // them (ApiModule/LogsModule get away with no-op bodies); WebModule
        // is the first module that needs the browser session this creates,
        // scoped to run.Id (see WebModule's own doc comment on why that
        // can't be plain instance state on a singleton).
        var usedModules = flow.Steps.Select(s => s.Module).Distinct()
            .Select(kind => _registry.Resolve(kind))
            .Where(m => m is not null)
            .Cast<IAutomationModule>()
            .ToList();

        var runLevelCtx = new ExecutionContext
        {
            RunId = run.Id,
            Variables = run.Variables,
            Credentials = credentials,
            RunCancellation = ct
        };

        try
        {
            // Setup runs inside the try so that if any module's Setup
            // throws (e.g. Playwright/browser launch failure), the finally
            // block below still runs: it decrements the concurrency gauge
            // and tears down whichever modules DID set up successfully
            // before the one that failed (WebModule.Teardown is a safe
            // no-op for a module whose Setup never ran).
            foreach (var module in usedModules)
                await module.Setup(runLevelCtx, ct);

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
                    // FR-OPS-02: CancellationToken.None, deliberately not ct
                    // — this outcome is already decided; a shutdown token
                    // that fired while evaluating the condition must not
                    // make EF's SaveChangesAsync throw before it durably
                    // records that decision (see the commit below for the
                    // same reasoning, which is what FR-OPS-02 actually
                    // requires: the current step's result survives a
                    // mid-step shutdown request).
                    await CommitRun(run, CancellationToken.None);
                    continue;
                }

                using var stepActivity = OrchestrationTelemetry.ActivitySource.StartActivity(
                    "FlowStep.Execute", ActivityKind.Internal);
                stepActivity?.SetTag("flow.step.name", step.Name);
                stepActivity?.SetTag("flow.step.module", step.Module.Value);
                stepActivity?.SetTag("flow.step.action", step.Action);

                var interpolatedParams = _variables.Interpolate(step.Params, run.Variables);
                var execCtx = new ExecutionContext
                {
                    RunId = run.Id,
                    Variables = run.Variables,
                    Credentials = credentials,
                    RunCancellation = ct
                };

                var dispatchStopwatch = Stopwatch.StartNew();
                var retryOutcome = await _retryHandler.ExecuteWithRetry(
                    innerCt => _dispatcher.Dispatch(step.Module, step.Action, interpolatedParams, execCtx, innerCt),
                    _defaultRetryPolicy, ct);
                dispatchStopwatch.Stop();

                var result = new StepResult(
                    step.Id, step.Name, step.Module, step.Action,
                    retryOutcome.FinalOutcome.Status, TimeSpan.Zero,
                    retryOutcome.FinalOutcome.Message, retryOutcome.FinalOutcome.Error, retryOutcome.FinalOutcome.Data,
                    retryOutcome.FinalOutcome.AiAttribution, retryOutcome.Attempts);

                OrchestrationTelemetry.StepDispatchDuration.Record(dispatchStopwatch.Elapsed.TotalMilliseconds,
                    new KeyValuePair<string, object?>("module", step.Module.Value),
                    new KeyValuePair<string, object?>("action", step.Action),
                    new KeyValuePair<string, object?>("status", result.Status.ToString()));
                stepActivity?.SetTag("flow.step.status", result.Status.ToString());

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

                // Commit THIS step's result now, before dispatching the next
                // step. CancellationToken.None, not ct — FR-OPS-02's actual
                // guarantee ("current step completes and commits before the
                // process exits") only holds if a shutdown token that fired
                // during this step's execution can't also abort the commit
                // of its own just-finished result.
                await CommitRun(run, CancellationToken.None);

                if (result.Status == StepStatus.Failed && !flow.ContinueOnFailure)
                {
                    _logger.LogWarning("FlowRun {FlowRunId} stopping early — step {StepName} failed and ContinueOnFailure is false",
                        run.Id.Value, step.Name);
                    break; // fail-fast default (Product Philosophy 4.2)
                }
            }

            run.Complete();
            await CommitRun(run, ct);

            OrchestrationTelemetry.FlowRunCompleted.Add(1,
                new KeyValuePair<string, object?>("status", run.Status.ToString()));
            runActivity?.SetTag("flow.run.status", run.Status.ToString());

            _logger.LogInformation("FlowRun {FlowRunId} finished with status {Status}", run.Id.Value, run.Status);
            return run;
        }
        finally
        {
            // CancellationToken.None, deliberately not ct — a run that's
            // being cancelled or already failed must still get its browser
            // session closed; a shutdown token firing here must not leak a
            // Chromium process. Each module's teardown is isolated so one
            // module failing to clean up doesn't skip the others or mask
            // the run's real outcome.
            foreach (var module in usedModules)
            {
                try
                {
                    await module.Teardown(runLevelCtx, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Teardown failed for module {Module} on FlowRun {FlowRunId}",
                        module.Kind, run.Id.Value);
                }
            }

            OrchestrationTelemetry.OrchestrationConcurrentExecutions.Add(-1);
        }
    }

    private async Task CommitRun(FlowRun run, CancellationToken ct)
    {
        await _runRepository.Save(run, ct);
        await using var uow = _uowFactory.Create();
        await uow.Commit(ct);
    }
}
