using Yukti.Application.Abstractions;
using Yukti.Application.Auditing;
using Yukti.Application.IdentityAccess;
using Yukti.Domain.Execution;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.SharedKernel;

namespace Yukti.Application.Execution;

public sealed record TriggerFlowRunCommand(
    FlowId FlowId, RunTrigger Trigger, IReadOnlyDictionary<string, object?>? VariableOverrides, TenantId TenantId, UserId TriggeredBy)
    : ICommand<FlowRunId>;

public sealed record CancelFlowRunCommand(FlowRunId RunId, UserId CancelledBy) : ICommand<bool>;

/// <summary>
/// Creates the FlowRun aggregate in Pending status and persists it. Actual
/// step-by-step execution is the Orchestration layer's job (FlowEngine) —
/// this handler's responsibility ends at "the run now durably exists,"
/// consistent with the API Layer / Orchestration Engine container split
/// (Volume 1 Part I §4.4).
/// </summary>
public sealed class TriggerFlowRunCommandHandler : AuditableCommandHandler<TriggerFlowRunCommand, FlowRunId>
{
    private readonly IFlowRunRepository _runs;
    private readonly IPermissionChecker _permissions;
    private readonly IUnitOfWorkFactory _uowFactory;

    public TriggerFlowRunCommandHandler(IFlowRunRepository runs, IPermissionChecker permissions, IUnitOfWorkFactory uowFactory, IAuditRepository audit)
        : base(audit)
    {
        _runs = runs;
        _permissions = permissions;
        _uowFactory = uowFactory;
    }

    protected override async Task<FlowRunId> HandleCore(TriggerFlowRunCommand cmd, CancellationToken ct)
    {
        await _permissions.EnsurePermission(cmd.TriggeredBy, Permission.FlowExecute, ct);

        var run = FlowRun.Create(cmd.FlowId, cmd.Trigger, cmd.TenantId, cmd.VariableOverrides);
        await _runs.Save(run, ct);
        await using var uow = _uowFactory.Create();
        await uow.Commit(ct);
        return run.Id;
    }
}

public sealed class CancelFlowRunCommandHandler : AuditableCommandHandler<CancelFlowRunCommand, bool>
{
    private readonly IFlowRunRepository _runs;
    private readonly IPermissionChecker _permissions;
    private readonly ITenantGuard _tenantGuard;
    private readonly IUnitOfWorkFactory _uowFactory;

    public CancelFlowRunCommandHandler(IFlowRunRepository runs, IPermissionChecker permissions, ITenantGuard tenantGuard, IUnitOfWorkFactory uowFactory, IAuditRepository audit)
        : base(audit)
    {
        _runs = runs;
        _permissions = permissions;
        _tenantGuard = tenantGuard;
        _uowFactory = uowFactory;
    }

    protected override async Task<bool> HandleCore(CancelFlowRunCommand cmd, CancellationToken ct)
    {
        await _permissions.EnsurePermission(cmd.CancelledBy, Permission.FlowExecute, ct);

        var run = await _runs.GetById(cmd.RunId, ct)
            ?? throw new InvalidOperationException($"FlowRun {cmd.RunId} not found.");
        _tenantGuard.EnsureAccessible(run.TenantId); // FR-TENANT-01 Layer 3
        run.Cancel();
        await _runs.Save(run, ct);
        await using var uow = _uowFactory.Create();
        await uow.Commit(ct);
        return true;
    }
}
