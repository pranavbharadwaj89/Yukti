using Yukti.Application.Abstractions;
using Yukti.Domain.Execution;
using Yukti.Domain.SharedKernel;

namespace Yukti.Application.Execution;

public sealed record TriggerFlowRunCommand(
    FlowId FlowId, RunTrigger Trigger, IReadOnlyDictionary<string, object?>? VariableOverrides, TenantId TenantId)
    : ICommand<FlowRunId>;

public sealed record CancelFlowRunCommand(FlowRunId RunId, UserId CancelledBy) : ICommand<bool>;

/// <summary>
/// Creates the FlowRun aggregate in Pending status and persists it. Actual
/// step-by-step execution is the Orchestration layer's job (FlowEngine) —
/// this handler's responsibility ends at "the run now durably exists,"
/// consistent with the API Layer / Orchestration Engine container split
/// (Volume 1 Part I §4.4).
/// </summary>
public sealed class TriggerFlowRunCommandHandler : ICommandHandler<TriggerFlowRunCommand, FlowRunId>
{
    private readonly IFlowRunRepository _runs;
    private readonly IUnitOfWorkFactory _uowFactory;

    public TriggerFlowRunCommandHandler(IFlowRunRepository runs, IUnitOfWorkFactory uowFactory)
    {
        _runs = runs;
        _uowFactory = uowFactory;
    }

    public async Task<FlowRunId> Handle(TriggerFlowRunCommand cmd, CancellationToken ct)
    {
        var run = FlowRun.Create(cmd.FlowId, cmd.Trigger, cmd.TenantId, cmd.VariableOverrides);
        await _runs.Save(run, ct);
        await using var uow = _uowFactory.Create();
        await uow.Commit(ct);
        return run.Id;
    }
}

public sealed class CancelFlowRunCommandHandler : ICommandHandler<CancelFlowRunCommand, bool>
{
    private readonly IFlowRunRepository _runs;
    private readonly IUnitOfWorkFactory _uowFactory;

    public CancelFlowRunCommandHandler(IFlowRunRepository runs, IUnitOfWorkFactory uowFactory)
    {
        _runs = runs;
        _uowFactory = uowFactory;
    }

    public async Task<bool> Handle(CancelFlowRunCommand cmd, CancellationToken ct)
    {
        var run = await _runs.GetById(cmd.RunId, ct)
            ?? throw new InvalidOperationException($"FlowRun {cmd.RunId} not found.");
        run.Cancel();
        await _runs.Save(run, ct);
        await using var uow = _uowFactory.Create();
        await uow.Commit(ct);
        return true;
    }
}
