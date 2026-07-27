using Yukti.Application.Abstractions;
using Yukti.Application.Auditing;
using Yukti.Application.IdentityAccess;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;

namespace Yukti.Application.FlowAuthoring;

public sealed record CreateFlowCommand(string Name, string? Description, TenantId TenantId, UserId AuthoredBy)
    : ICommand<FlowId>;

public sealed record AddFlowStepCommand(
    FlowId FlowId, string StepName, ModuleKind Module, string Action,
    IReadOnlyDictionary<string, object?> Params, string? SaveAs, string? When, UserId RequestedBy) : ICommand<bool>;

public sealed record PublishFlowCommand(FlowId FlowId, UserId PublishedBy) : ICommand<FlowPublishResult>;

public sealed class CreateFlowCommandHandler : AuditableCommandHandler<CreateFlowCommand, FlowId>
{
    private readonly IFlowRepository _flows;
    private readonly IPermissionChecker _permissions;
    private readonly IUnitOfWorkFactory _uowFactory;

    public CreateFlowCommandHandler(IFlowRepository flows, IPermissionChecker permissions, IUnitOfWorkFactory uowFactory, IAuditRepository audit, ITenantContextAccessor tenantAccessor)
        : base(audit, tenantAccessor)
    {
        _flows = flows;
        _permissions = permissions;
        _uowFactory = uowFactory;
    }

    protected override async Task<FlowId> HandleCore(CreateFlowCommand cmd, CancellationToken ct)
    {
        await _permissions.EnsurePermission(cmd.AuthoredBy, Permission.FlowCreate, ct);

        var flow = Flow.CreateDraft(cmd.Name, cmd.Description, cmd.TenantId, cmd.AuthoredBy);
        await _flows.Save(flow, ct);
        await using var uow = _uowFactory.Create();
        await uow.Commit(ct);
        return flow.Id;
    }
}

public sealed class AddFlowStepCommandHandler : AuditableCommandHandler<AddFlowStepCommand, bool>
{
    private readonly IFlowRepository _flows;
    private readonly IPermissionChecker _permissions;
    private readonly ITenantGuard _tenantGuard;
    private readonly IUnitOfWorkFactory _uowFactory;

    public AddFlowStepCommandHandler(IFlowRepository flows, IPermissionChecker permissions, ITenantGuard tenantGuard, IUnitOfWorkFactory uowFactory, IAuditRepository audit, ITenantContextAccessor tenantAccessor)
        : base(audit, tenantAccessor)
    {
        _flows = flows;
        _permissions = permissions;
        _tenantGuard = tenantGuard;
        _uowFactory = uowFactory;
    }

    protected override async Task<bool> HandleCore(AddFlowStepCommand cmd, CancellationToken ct)
    {
        await _permissions.EnsurePermission(cmd.RequestedBy, Permission.FlowEdit, ct);

        var flow = await _flows.GetById(cmd.FlowId, ct)
            ?? throw new InvalidOperationException($"Flow {cmd.FlowId} not found.");
        _tenantGuard.EnsureAccessible(flow.TenantId); // FR-TENANT-01 Layer 3, redundant with the repository's own tenant filter by design

        flow.AddStep(cmd.StepName, cmd.Module, cmd.Action, cmd.Params, cmd.SaveAs, cmd.When);

        await _flows.Save(flow, ct);
        await using var uow = _uowFactory.Create();
        await uow.Commit(ct);
        return true;
    }
}

public sealed class PublishFlowCommandHandler : AuditableCommandHandler<PublishFlowCommand, FlowPublishResult>
{
    private readonly IFlowRepository _flows;
    private readonly IModuleActionResolver _resolver;
    private readonly IPermissionChecker _permissions;
    private readonly ITenantGuard _tenantGuard;
    private readonly IUnitOfWorkFactory _uowFactory;

    public PublishFlowCommandHandler(IFlowRepository flows, IModuleActionResolver resolver, IPermissionChecker permissions, ITenantGuard tenantGuard, IUnitOfWorkFactory uowFactory, IAuditRepository audit, ITenantContextAccessor tenantAccessor)
        : base(audit, tenantAccessor)
    {
        _flows = flows;
        _resolver = resolver;
        _permissions = permissions;
        _tenantGuard = tenantGuard;
        _uowFactory = uowFactory;
    }

    protected override async Task<FlowPublishResult> HandleCore(PublishFlowCommand cmd, CancellationToken ct)
    {
        await _permissions.EnsurePermission(cmd.PublishedBy, Permission.FlowPublish, ct);

        var flow = await _flows.GetById(cmd.FlowId, ct)
            ?? throw new InvalidOperationException($"Flow {cmd.FlowId} not found.");
        _tenantGuard.EnsureAccessible(flow.TenantId); // FR-TENANT-01 Layer 3

        var result = flow.Publish(_resolver);

        if (result.Succeeded)
        {
            await _flows.Save(flow, ct);
            await using var uow = _uowFactory.Create();
            await uow.Commit(ct); // persists state + dispatches raised domain events together
        }

        return result;
    }
}
