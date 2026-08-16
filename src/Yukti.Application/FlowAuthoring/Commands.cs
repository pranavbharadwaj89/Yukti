using Yukti.Application.Abstractions;
using Yukti.Application.Auditing;
using Yukti.Application.IdentityAccess;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;

namespace Yukti.Application.FlowAuthoring;

public sealed record CreateFlowCommand(string Name, string? Description, TenantId TenantId, UserId AuthoredBy, ProjectId? ProjectId = null)
    : ICommand<FlowId>;

public sealed record AddFlowStepCommand(
    FlowId FlowId, string StepName, ModuleKind Module, string Action,
    IReadOnlyDictionary<string, object?> Params, string? SaveAs, string? When, UserId RequestedBy) : ICommand<bool>;

public sealed record PublishFlowCommand(FlowId FlowId, UserId PublishedBy) : ICommand<FlowPublishResult>;

public sealed class CreateFlowCommandHandler : AuditableCommandHandler<CreateFlowCommand, FlowId>
{
    private readonly IFlowRepository _flows;
    private readonly IPermissionChecker _permissions;

    public CreateFlowCommandHandler(IFlowRepository flows, IPermissionChecker permissions, IUnitOfWorkFactory uowFactory, IAuditRepository audit, ITenantContextAccessor tenantAccessor)
        : base(audit, tenantAccessor, uowFactory)
    {
        _flows = flows;
        _permissions = permissions;
    }

    protected override async Task<FlowId> HandleCore(CreateFlowCommand cmd, CancellationToken ct)
    {
        await _permissions.EnsurePermission(cmd.AuthoredBy, Permission.FlowCreate, ct);

        var flow = Flow.CreateDraft(cmd.Name, cmd.Description, cmd.TenantId, cmd.AuthoredBy, cmd.ProjectId);
        await _flows.Save(flow, ct);
        // FR-OPS-03: no commit here — AuditableCommandHandler.Handle commits
        // this alongside the audit entry, once, after HandleCore returns.
        return flow.Id;
    }
}

public sealed class AddFlowStepCommandHandler : AuditableCommandHandler<AddFlowStepCommand, bool>
{
    private readonly IFlowRepository _flows;
    private readonly IPermissionChecker _permissions;
    private readonly ITenantGuard _tenantGuard;

    public AddFlowStepCommandHandler(IFlowRepository flows, IPermissionChecker permissions, ITenantGuard tenantGuard, IUnitOfWorkFactory uowFactory, IAuditRepository audit, ITenantContextAccessor tenantAccessor)
        : base(audit, tenantAccessor, uowFactory)
    {
        _flows = flows;
        _permissions = permissions;
        _tenantGuard = tenantGuard;
    }

    protected override async Task<bool> HandleCore(AddFlowStepCommand cmd, CancellationToken ct)
    {
        await _permissions.EnsurePermission(cmd.RequestedBy, Permission.FlowEdit, ct);

        var flow = await _flows.GetById(cmd.FlowId, ct)
            ?? throw new InvalidOperationException($"Flow {cmd.FlowId} not found.");
        _tenantGuard.EnsureAccessible(flow.TenantId); // FR-TENANT-01 Layer 3, redundant with the repository's own tenant filter by design

        flow.AddStep(cmd.StepName, cmd.Module, cmd.Action, cmd.Params, cmd.SaveAs, cmd.When);

        await _flows.Save(flow, ct);
        // FR-OPS-03: no commit here — see CreateFlowCommandHandler's comment.
        return true;
    }
}

public sealed class PublishFlowCommandHandler : AuditableCommandHandler<PublishFlowCommand, FlowPublishResult>
{
    private readonly IFlowRepository _flows;
    private readonly IModuleActionResolver _resolver;
    private readonly IPermissionChecker _permissions;
    private readonly ITenantGuard _tenantGuard;

    public PublishFlowCommandHandler(IFlowRepository flows, IModuleActionResolver resolver, IPermissionChecker permissions, ITenantGuard tenantGuard, IUnitOfWorkFactory uowFactory, IAuditRepository audit, ITenantContextAccessor tenantAccessor)
        : base(audit, tenantAccessor, uowFactory)
    {
        _flows = flows;
        _resolver = resolver;
        _permissions = permissions;
        _tenantGuard = tenantGuard;
    }

    protected override async Task<FlowPublishResult> HandleCore(PublishFlowCommand cmd, CancellationToken ct)
    {
        await _permissions.EnsurePermission(cmd.PublishedBy, Permission.FlowPublish, ct);

        var flow = await _flows.GetById(cmd.FlowId, ct)
            ?? throw new InvalidOperationException($"Flow {cmd.FlowId} not found.");
        _tenantGuard.EnsureAccessible(flow.TenantId); // FR-TENANT-01 Layer 3

        var result = flow.Publish(_resolver);

        // FR-OPS-03: no commit here even when Succeeded — the outer
        // wrapper commits (staged state if Succeeded, nothing but the
        // audit entry otherwise) exactly once either way.
        if (result.Succeeded)
            await _flows.Save(flow, ct);

        return result;
    }
}
