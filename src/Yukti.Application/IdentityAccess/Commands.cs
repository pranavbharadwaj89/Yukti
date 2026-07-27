using Yukti.Application.Abstractions;
using Yukti.Application.Auditing;
using Yukti.Domain.Auditing;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.SharedKernel;

namespace Yukti.Application.IdentityAccess;

/// <summary>
/// FR-AUTHZ-02: "Every command handler calls EnsurePermission as its first
/// meaningful statement." Always re-reads the User's current RoleIds and
/// each Role's current Permissions from the repositories — never trusts a
/// cached claim — so a permission revoked mid-session is denied on the very
/// next request (FR-AUTHZ-04), not merely "within one request" as the FR's
/// minimum bar states.
/// </summary>
public interface IPermissionChecker
{
    Task EnsurePermission(UserId userId, Permission required, CancellationToken ct);
}

public sealed class PermissionChecker : IPermissionChecker
{
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;

    public PermissionChecker(IUserRepository users, IRoleRepository roles)
    {
        _users = users;
        _roles = roles;
    }

    public async Task EnsurePermission(UserId userId, Permission required, CancellationToken ct)
    {
        var user = await _users.GetById(userId, ct)
            ?? throw new ForbiddenException($"User {userId} does not exist.");

        // FR-OPS-03: one round trip for every role the user holds, not one
        // round trip per role — see IRoleRepository.GetByIds' doc comment.
        var roles = await _roles.GetByIds(user.RoleIds.ToList(), ct);
        if (roles.Any(role => role.Has(required)))
            return;

        throw new ForbiddenException($"User {userId} lacks required permission '{required}'.");
    }
}

public sealed record RegisterUserCommand(
    string Email, [property: SensitiveValue] string Password, string DisplayName, TenantId TenantId, RoleId InitialRoleId)
    : ICommand<UserId>;

public sealed record AssignRoleCommand(UserId UserId, RoleId RoleId, UserId AssignedBy) : ICommand<bool>;

public sealed record UpdateRolePermissionsCommand(RoleId RoleId, IReadOnlySet<Permission> Permissions, UserId UpdatedBy) : ICommand<int>;

public sealed class RegisterUserCommandHandler : AuditableCommandHandler<RegisterUserCommand, UserId>
{
    private readonly IUserRepository _users;
    private readonly IAuthBypassUserLookup _authBypass;
    private readonly ITenantSessionInitializer _tenantSession;
    private readonly IPasswordHasher _hasher;

    public RegisterUserCommandHandler(IUserRepository users, IAuthBypassUserLookup authBypass,
        ITenantSessionInitializer tenantSession, IPasswordHasher hasher,
        IUnitOfWorkFactory uowFactory, IAuditRepository audit, ITenantContextAccessor tenantAccessor)
        : base(audit, tenantAccessor, uowFactory)
    {
        _users = users;
        _authBypass = authBypass;
        _tenantSession = tenantSession;
        _hasher = hasher;
    }

    protected override async Task<UserId> HandleCore(RegisterUserCommand cmd, CancellationToken ct)
    {
        // FR-TENANT-01/FR-DB-02 fallout: the duplicate-email check must see
        // across tenants — there is no tenant context yet for a brand-new
        // self-registration, and users' RLS policy has no permissive
        // branch for that (see IAuthBypassUserLookup's doc comment).
        if (await _authBypass.GetByEmail(cmd.Email, ct) is not null)
            throw new DomainException($"Email '{cmd.Email}' is already registered.");

        // This request is minting cmd.TenantId itself — no JWT-derived
        // middleware has set app.current_tenant_id for it yet, so without
        // this, the INSERT below fails RLS's WITH CHECK (see
        // ITenantSessionInitializer's doc comment). A real round trip of
        // its own (raw SQL, not part of the change-tracked commit below) —
        // unavoidable for self-registration specifically, since no tenant
        // exists yet for the JWT-claim-based per-request middleware to have
        // set this from.
        await _tenantSession.EstablishTenantContext(cmd.TenantId, ct);

        var user = User.Register(cmd.Email, cmd.DisplayName, cmd.TenantId, _hasher.Hash(cmd.Password));
        user.AssignRole(cmd.InitialRoleId);

        await _users.Save(user, ct);
        // FR-OPS-03: no commit here — see CreateFlowCommandHandler's comment.
        return user.Id;
    }
}

public sealed class AssignRoleCommandHandler : AuditableCommandHandler<AssignRoleCommand, bool>
{
    private readonly IUserRepository _users;
    private readonly IPermissionChecker _permissions;

    public AssignRoleCommandHandler(IUserRepository users, IPermissionChecker permissions, IUnitOfWorkFactory uowFactory, IAuditRepository audit, ITenantContextAccessor tenantAccessor)
        : base(audit, tenantAccessor, uowFactory)
    {
        _users = users;
        _permissions = permissions;
    }

    protected override async Task<bool> HandleCore(AssignRoleCommand cmd, CancellationToken ct)
    {
        await _permissions.EnsurePermission(cmd.AssignedBy, Permission.UserManage, ct);

        var user = await _users.GetById(cmd.UserId, ct)
            ?? throw new InvalidOperationException($"User {cmd.UserId} not found.");
        user.AssignRole(cmd.RoleId);

        await _users.Save(user, ct);
        // FR-OPS-03: no commit here — see CreateFlowCommandHandler's comment.
        return true;
    }
}

public sealed class UpdateRolePermissionsCommandHandler : AuditableCommandHandler<UpdateRolePermissionsCommand, int>
{
    private readonly IRoleRepository _roles;
    private readonly IPermissionChecker _permissions;

    public UpdateRolePermissionsCommandHandler(IRoleRepository roles, IPermissionChecker permissions, IUnitOfWorkFactory uowFactory, IAuditRepository audit, ITenantContextAccessor tenantAccessor)
        : base(audit, tenantAccessor, uowFactory)
    {
        _roles = roles;
        _permissions = permissions;
    }

    protected override async Task<int> HandleCore(UpdateRolePermissionsCommand cmd, CancellationToken ct)
    {
        await _permissions.EnsurePermission(cmd.UpdatedBy, Permission.UserManage, ct);

        var role = await _roles.GetById(cmd.RoleId, ct)
            ?? throw new InvalidOperationException($"Role {cmd.RoleId} not found.");
        role.UpdatePermissions(cmd.Permissions);

        await _roles.Save(role, ct);
        // FR-OPS-03: no commit here — see CreateFlowCommandHandler's comment.
        return role.Version;
    }
}
