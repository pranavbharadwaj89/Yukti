using Yukti.Application.Abstractions;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;

namespace Yukti.Application.ModulePlugin;

public sealed record RegisterModuleCommand(
    ModuleKind Kind, string DisplayName, TrustTier Trust,
    IReadOnlyList<ActionSchema> Actions, string ContractVersion, UserId RegisteredBy, TenantId? TenantId = null)
    : ICommand<ModuleRegistrationId>;

public sealed class RegisterModuleCommandHandler : ICommandHandler<RegisterModuleCommand, ModuleRegistrationId>
{
    private readonly IModuleRegistrationRepository _registrations;
    private readonly IUnitOfWorkFactory _uowFactory;

    public RegisterModuleCommandHandler(IModuleRegistrationRepository registrations, IUnitOfWorkFactory uowFactory)
    {
        _registrations = registrations;
        _uowFactory = uowFactory;
    }

    public async Task<ModuleRegistrationId> Handle(RegisterModuleCommand cmd, CancellationToken ct)
    {
        var registration = new ModuleRegistration(cmd.Kind, cmd.DisplayName, cmd.Trust, cmd.ContractVersion, cmd.TenantId);
        foreach (var action in cmd.Actions)
            registration.RegisterAction(action);

        await _registrations.Save(registration, ct);
        await using var uow = _uowFactory.Create();
        await uow.Commit(ct);
        return registration.Id;
    }
}
