using Yukti.Application.Abstractions;
using Yukti.Application.Auditing;
using Yukti.Application.IdentityAccess;
using Yukti.Domain.ApiTesting;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.SharedKernel;

namespace Yukti.Application.ApiTesting;

/// <summary>
/// API-T9: the one implementation of IRequestTemplateExistsChecker — works identically against
/// either IRequestTemplateRepository implementation (in-memory for tests/CLI, EF for the real
/// API), the same "the port is the only thing that matters" reasoning ModuleActionResolver's own
/// doc comment already states for the module/action equivalent. No reason for two copies when
/// GetById + a tenant check is all either backing store needs.
/// </summary>
public sealed class RequestTemplateExistsChecker : IRequestTemplateExistsChecker
{
    private readonly IRequestTemplateRepository _templates;

    public RequestTemplateExistsChecker(IRequestTemplateRepository templates) => _templates = templates;

    public async Task<bool> ExistsAsync(string requestTemplateId, TenantId tenantId, CancellationToken ct)
    {
        if (!Guid.TryParse(requestTemplateId, out var guid))
            return false;

        var template = await _templates.GetById(new RequestTemplateId(guid), ct);
        return template is not null && template.TenantId == tenantId;
    }
}

public sealed record CreateRequestTemplateCommand(
    string Name, TenantId TenantId, string? BaseUrl,
    IReadOnlyDictionary<string, object?>? Headers, object? Auth, int? DefaultTimeoutMs, UserId RequestedBy)
    : ICommand<RequestTemplateId>;

public sealed record UpdateRequestTemplateCommand(
    RequestTemplateId TemplateId, string Name, string? BaseUrl,
    IReadOnlyDictionary<string, object?>? Headers, object? Auth, int? DefaultTimeoutMs, UserId RequestedBy)
    : ICommand<bool>;

public sealed record DeleteRequestTemplateCommand(RequestTemplateId TemplateId, UserId RequestedBy) : ICommand<bool>;

public sealed class CreateRequestTemplateCommandHandler : AuditableCommandHandler<CreateRequestTemplateCommand, RequestTemplateId>
{
    private readonly IRequestTemplateRepository _templates;
    private readonly IPermissionChecker _permissions;

    public CreateRequestTemplateCommandHandler(IRequestTemplateRepository templates, IPermissionChecker permissions, IUnitOfWorkFactory uowFactory, IAuditRepository audit, ITenantContextAccessor tenantAccessor)
        : base(audit, tenantAccessor, uowFactory)
    {
        _templates = templates;
        _permissions = permissions;
    }

    protected override async Task<RequestTemplateId> HandleCore(CreateRequestTemplateCommand cmd, CancellationToken ct)
    {
        await _permissions.EnsurePermission(cmd.RequestedBy, Permission.RequestTemplateManage, ct);

        var template = RequestTemplate.Create(cmd.Name, cmd.TenantId, cmd.BaseUrl, cmd.Headers, cmd.Auth, cmd.DefaultTimeoutMs);
        await _templates.Save(template, ct);
        return template.Id;
    }
}

public sealed class UpdateRequestTemplateCommandHandler : AuditableCommandHandler<UpdateRequestTemplateCommand, bool>
{
    private readonly IRequestTemplateRepository _templates;
    private readonly IPermissionChecker _permissions;
    private readonly ITenantGuard _tenantGuard;

    public UpdateRequestTemplateCommandHandler(IRequestTemplateRepository templates, IPermissionChecker permissions, ITenantGuard tenantGuard, IUnitOfWorkFactory uowFactory, IAuditRepository audit, ITenantContextAccessor tenantAccessor)
        : base(audit, tenantAccessor, uowFactory)
    {
        _templates = templates;
        _permissions = permissions;
        _tenantGuard = tenantGuard;
    }

    protected override async Task<bool> HandleCore(UpdateRequestTemplateCommand cmd, CancellationToken ct)
    {
        await _permissions.EnsurePermission(cmd.RequestedBy, Permission.RequestTemplateManage, ct);

        var template = await _templates.GetById(cmd.TemplateId, ct)
            ?? throw new InvalidOperationException($"RequestTemplate {cmd.TemplateId} not found.");
        _tenantGuard.EnsureAccessible(template.TenantId);

        template.Update(cmd.Name, cmd.BaseUrl, cmd.Headers, cmd.Auth, cmd.DefaultTimeoutMs);
        await _templates.Save(template, ct);
        return true;
    }
}

public sealed class DeleteRequestTemplateCommandHandler : AuditableCommandHandler<DeleteRequestTemplateCommand, bool>
{
    private readonly IRequestTemplateRepository _templates;
    private readonly IPermissionChecker _permissions;
    private readonly ITenantGuard _tenantGuard;

    public DeleteRequestTemplateCommandHandler(IRequestTemplateRepository templates, IPermissionChecker permissions, ITenantGuard tenantGuard, IUnitOfWorkFactory uowFactory, IAuditRepository audit, ITenantContextAccessor tenantAccessor)
        : base(audit, tenantAccessor, uowFactory)
    {
        _templates = templates;
        _permissions = permissions;
        _tenantGuard = tenantGuard;
    }

    protected override async Task<bool> HandleCore(DeleteRequestTemplateCommand cmd, CancellationToken ct)
    {
        await _permissions.EnsurePermission(cmd.RequestedBy, Permission.RequestTemplateManage, ct);

        var template = await _templates.GetById(cmd.TemplateId, ct)
            ?? throw new InvalidOperationException($"RequestTemplate {cmd.TemplateId} not found.");
        _tenantGuard.EnsureAccessible(template.TenantId);

        await _templates.Delete(template, ct);
        return true;
    }
}
