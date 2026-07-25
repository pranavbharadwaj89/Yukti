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

    public FlowEngine(
        IModuleDispatcher dispatcher, IVariableStore variables, IRetryFlakeHandler retryHandler,
        IFlowRunRepository runRepository, IUnitOfWorkFactory uowFactory, RetryPolicy? defaultRetryPolicy = null)
    {
        _dispatcher = dispatcher;
        _variables = variables;
        _retryHandler = retryHandler;
        _runRepository = runRepository;
        _uowFactory = uowFactory;
        _defaultRetryPolicy = defaultRetryPolicy ?? new RetryPolicy(MaxAttempts: 2, InitialBackoff: TimeSpan.FromMilliseconds(200), BackoffMultiplier: 2.0);
    }

    public async Task<FlowRun> Execute(Flow flow, FlowRun run, ICredentialResolver credentials, CancellationToken ct)
    {
        run.Start();
        await CommitRun(run, ct);

        foreach (var step in flow.Steps.OrderBy(s => s.Order))
        {
            ct.ThrowIfCancellationRequested();

            if (!_variables.EvaluateCondition(step.WhenCondition, run.Variables))
            {
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

            if (step.SaveAs is not null)
                run.BindVariable(step.SaveAs, result.Data);

            // Commit THIS step's result now, before dispatching the next step.
            await CommitRun(run, ct);

            if (result.Status == StepStatus.Failed && !flow.ContinueOnFailure)
                break; // fail-fast default (Product Philosophy 4.2)
        }

        run.Complete();
        await CommitRun(run, ct);
        return run;
    }

    private async Task CommitRun(FlowRun run, CancellationToken ct)
    {
        await _runRepository.Save(run, ct);
        await using var uow = _uowFactory.Create();
        await uow.Commit(ct);
    }
}
