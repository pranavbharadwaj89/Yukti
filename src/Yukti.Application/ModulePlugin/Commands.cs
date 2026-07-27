using Yukti.Application.Abstractions;
using Yukti.Application.Auditing;
using Yukti.Application.IdentityAccess;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;

namespace Yukti.Application.ModulePlugin;

public sealed record RegisterModuleCommand(
    ModuleKind Kind, string DisplayName, TrustTier Trust,
    IReadOnlyList<ActionSchema> Actions, string ContractVersion, UserId RegisteredBy, TenantId? TenantId = null)
    : ICommand<ModuleRegistrationId>;

public sealed class RegisterModuleCommandHandler : AuditableCommandHandler<RegisterModuleCommand, ModuleRegistrationId>
{
    private readonly IModuleRegistrationRepository _registrations;
    private readonly IPermissionChecker _permissions;
    private readonly IUnitOfWorkFactory _uowFactory;

    public RegisterModuleCommandHandler(IModuleRegistrationRepository registrations, IPermissionChecker permissions, IUnitOfWorkFactory uowFactory, IAuditRepository audit, ITenantContextAccessor tenantAccessor)
        : base(audit, tenantAccessor)
    {
        _registrations = registrations;
        _permissions = permissions;
        _uowFactory = uowFactory;
    }

    protected override async Task<ModuleRegistrationId> HandleCore(RegisterModuleCommand cmd, CancellationToken ct)
    {
        await _permissions.EnsurePermission(cmd.RegisteredBy, Permission.ModuleManage, ct);

        var registration = new ModuleRegistration(cmd.Kind, cmd.DisplayName, cmd.Trust, cmd.ContractVersion, cmd.TenantId);
        foreach (var action in cmd.Actions)
            registration.RegisterAction(action);

        await _registrations.Save(registration, ct);
        await using var uow = _uowFactory.Create();
        await uow.Commit(ct);
        return registration.Id;
    }
}
