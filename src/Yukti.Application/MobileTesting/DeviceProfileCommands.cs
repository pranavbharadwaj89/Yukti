using Yukti.Application.Abstractions;
using Yukti.Application.Auditing;
using Yukti.Application.IdentityAccess;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.MobileTesting;
using Yukti.Domain.SharedKernel;

namespace Yukti.Application.MobileTesting;

/// <summary>
/// Mobile Testing Module Spec, MOB-T2: the one implementation of IDeviceProfileExistsChecker —
/// works identically against either IDeviceProfileRepository implementation (in-memory for
/// tests/CLI, EF for the real API), the same "the port is the only thing that matters" reasoning
/// RequestTemplateExistsChecker's own doc comment already states for RequestTemplate. Checked by
/// name, not id, matching GetByName's own reasoning on the repository interface.
/// </summary>
public sealed class DeviceProfileExistsChecker : IDeviceProfileExistsChecker
{
    private readonly IDeviceProfileRepository _profiles;

    public DeviceProfileExistsChecker(IDeviceProfileRepository profiles) => _profiles = profiles;

    public async Task<bool> ExistsAsync(string profileName, TenantId tenantId, CancellationToken ct)
    {
        var profile = await _profiles.GetByName(profileName, tenantId, ct);
        return profile is not null;
    }
}

public sealed record CreateDeviceProfileCommand(
    string Name, TenantId TenantId, MobilePlatform Platform, string? PlatformVersion, string? DeviceName,
    string? App, string AppiumUrl, DeviceEndpointKind EndpointKind, string? FarmCredentialRef,
    ResetStrategy Reset, TimeSpan? SessionTimeout, IReadOnlyDictionary<string, object?>? ExtraCapabilities,
    UserId RequestedBy) : ICommand<DeviceProfileId>;

public sealed record UpdateDeviceProfileCommand(
    DeviceProfileId ProfileId, string Name, MobilePlatform Platform, string? PlatformVersion, string? DeviceName,
    string? App, string AppiumUrl, DeviceEndpointKind EndpointKind, string? FarmCredentialRef,
    ResetStrategy Reset, TimeSpan SessionTimeout, IReadOnlyDictionary<string, object?>? ExtraCapabilities,
    UserId RequestedBy) : ICommand<bool>;

public sealed record DeleteDeviceProfileCommand(DeviceProfileId ProfileId, UserId RequestedBy) : ICommand<bool>;

/// <summary>MOB-T2's own checklist: "Admin UI with a 'test profile' action that opens a session,
/// takes a screenshot and tears down." No permission beyond DeviceProfileManage — testing a
/// profile is part of managing it, not a separate capability.</summary>
public sealed record TestDeviceProfileCommand(DeviceProfileId ProfileId, UserId RequestedBy) : ICommand<DeviceProfileTestResult>;

public sealed class CreateDeviceProfileCommandHandler : AuditableCommandHandler<CreateDeviceProfileCommand, DeviceProfileId>
{
    private readonly IDeviceProfileRepository _profiles;
    private readonly IPermissionChecker _permissions;

    public CreateDeviceProfileCommandHandler(IDeviceProfileRepository profiles, IPermissionChecker permissions, IUnitOfWorkFactory uowFactory, IAuditRepository audit, ITenantContextAccessor tenantAccessor)
        : base(audit, tenantAccessor, uowFactory)
    {
        _profiles = profiles;
        _permissions = permissions;
    }

    protected override async Task<DeviceProfileId> HandleCore(CreateDeviceProfileCommand cmd, CancellationToken ct)
    {
        await _permissions.EnsurePermission(cmd.RequestedBy, Permission.DeviceProfileManage, ct);

        var profile = DeviceProfile.Create(
            cmd.Name, cmd.TenantId, cmd.Platform, cmd.PlatformVersion, cmd.DeviceName, cmd.App, cmd.AppiumUrl,
            cmd.EndpointKind, cmd.FarmCredentialRef, cmd.Reset, cmd.SessionTimeout, cmd.ExtraCapabilities);
        await _profiles.Save(profile, ct);
        return profile.Id;
    }
}

public sealed class UpdateDeviceProfileCommandHandler : AuditableCommandHandler<UpdateDeviceProfileCommand, bool>
{
    private readonly IDeviceProfileRepository _profiles;
    private readonly IPermissionChecker _permissions;
    private readonly ITenantGuard _tenantGuard;

    public UpdateDeviceProfileCommandHandler(IDeviceProfileRepository profiles, IPermissionChecker permissions, ITenantGuard tenantGuard, IUnitOfWorkFactory uowFactory, IAuditRepository audit, ITenantContextAccessor tenantAccessor)
        : base(audit, tenantAccessor, uowFactory)
    {
        _profiles = profiles;
        _permissions = permissions;
        _tenantGuard = tenantGuard;
    }

    protected override async Task<bool> HandleCore(UpdateDeviceProfileCommand cmd, CancellationToken ct)
    {
        await _permissions.EnsurePermission(cmd.RequestedBy, Permission.DeviceProfileManage, ct);

        var profile = await _profiles.GetById(cmd.ProfileId, ct)
            ?? throw new InvalidOperationException($"DeviceProfile {cmd.ProfileId} not found.");
        _tenantGuard.EnsureAccessible(profile.TenantId);

        profile.Update(
            cmd.Name, cmd.Platform, cmd.PlatformVersion, cmd.DeviceName, cmd.App, cmd.AppiumUrl,
            cmd.EndpointKind, cmd.FarmCredentialRef, cmd.Reset, cmd.SessionTimeout, cmd.ExtraCapabilities);
        await _profiles.Save(profile, ct);
        return true;
    }
}

public sealed class DeleteDeviceProfileCommandHandler : AuditableCommandHandler<DeleteDeviceProfileCommand, bool>
{
    private readonly IDeviceProfileRepository _profiles;
    private readonly IPermissionChecker _permissions;
    private readonly ITenantGuard _tenantGuard;

    public DeleteDeviceProfileCommandHandler(IDeviceProfileRepository profiles, IPermissionChecker permissions, ITenantGuard tenantGuard, IUnitOfWorkFactory uowFactory, IAuditRepository audit, ITenantContextAccessor tenantAccessor)
        : base(audit, tenantAccessor, uowFactory)
    {
        _profiles = profiles;
        _permissions = permissions;
        _tenantGuard = tenantGuard;
    }

    protected override async Task<bool> HandleCore(DeleteDeviceProfileCommand cmd, CancellationToken ct)
    {
        await _permissions.EnsurePermission(cmd.RequestedBy, Permission.DeviceProfileManage, ct);

        var profile = await _profiles.GetById(cmd.ProfileId, ct)
            ?? throw new InvalidOperationException($"DeviceProfile {cmd.ProfileId} not found.");
        _tenantGuard.EnsureAccessible(profile.TenantId);

        await _profiles.Delete(profile, ct);
        return true;
    }
}

/// <summary>MOB-T2: "a 'test profile' action that opens a session, takes a screenshot and tears
/// down, so a misconfiguration is caught at setup rather than in a failing test run." Not
/// Auditable — this reads/exercises infrastructure, it changes nothing about the profile itself,
/// so there is no state change for the audit trail to record (same reasoning queries aren't
/// audited anywhere else in this codebase).</summary>
public sealed class TestDeviceProfileCommandHandler
{
    private readonly IDeviceProfileRepository _profiles;
    private readonly IPermissionChecker _permissions;
    private readonly ITenantGuard _tenantGuard;
    private readonly IDeviceProfileTester _tester;

    public TestDeviceProfileCommandHandler(IDeviceProfileRepository profiles, IPermissionChecker permissions, ITenantGuard tenantGuard, IDeviceProfileTester tester)
    {
        _profiles = profiles;
        _permissions = permissions;
        _tenantGuard = tenantGuard;
        _tester = tester;
    }

    public async Task<DeviceProfileTestResult> Handle(TestDeviceProfileCommand cmd, CancellationToken ct)
    {
        await _permissions.EnsurePermission(cmd.RequestedBy, Permission.DeviceProfileManage, ct);

        var profile = await _profiles.GetById(cmd.ProfileId, ct)
            ?? throw new InvalidOperationException($"DeviceProfile {cmd.ProfileId} not found.");
        _tenantGuard.EnsureAccessible(profile.TenantId);

        return await _tester.TestConnection(profile, ct);
    }
}
