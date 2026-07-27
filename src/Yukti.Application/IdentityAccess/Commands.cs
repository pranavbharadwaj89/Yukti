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

        foreach (var roleId in user.RoleIds)
        {
            var role = await _roles.GetById(roleId, ct);
            if (role is not null && role.Has(required))
                return;
        }

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
    private readonly IPasswordHasher _hasher;
    private readonly IUnitOfWorkFactory _uowFactory;

    public RegisterUserCommandHandler(IUserRepository users, IPasswordHasher hasher, IUnitOfWorkFactory uowFactory, IAuditRepository audit, ITenantContextAccessor tenantAccessor)
        : base(audit, tenantAccessor)
    {
        _users = users;
        _hasher = hasher;
        _uowFactory = uowFactory;
    }

    protected override async Task<UserId> HandleCore(RegisterUserCommand cmd, CancellationToken ct)
    {
        if (await _users.GetByEmail(cmd.Email, ct) is not null)
            throw new DomainException($"Email '{cmd.Email}' is already registered.");

        var user = User.Register(cmd.Email, cmd.DisplayName, cmd.TenantId, _hasher.Hash(cmd.Password));
        user.AssignRole(cmd.InitialRoleId);

        await _users.Save(user, ct);
        await using var uow = _uowFactory.Create();
        await uow.Commit(ct);
        return user.Id;
    }
}

public sealed class AssignRoleCommandHandler : AuditableCommandHandler<AssignRoleCommand, bool>
{
    private readonly IUserRepository _users;
    private readonly IPermissionChecker _permissions;
    private readonly IUnitOfWorkFactory _uowFactory;

    public AssignRoleCommandHandler(IUserRepository users, IPermissionChecker permissions, IUnitOfWorkFactory uowFactory, IAuditRepository audit, ITenantContextAccessor tenantAccessor)
        : base(audit, tenantAccessor)
    {
        _users = users;
        _permissions = permissions;
        _uowFactory = uowFactory;
    }

    protected override async Task<bool> HandleCore(AssignRoleCommand cmd, CancellationToken ct)
    {
        await _permissions.EnsurePermission(cmd.AssignedBy, Permission.UserManage, ct);

        var user = await _users.GetById(cmd.UserId, ct)
            ?? throw new InvalidOperationException($"User {cmd.UserId} not found.");
        user.AssignRole(cmd.RoleId);

        await _users.Save(user, ct);
        await using var uow = _uowFactory.Create();
        await uow.Commit(ct);
        return true;
    }
}

public sealed class UpdateRolePermissionsCommandHandler : AuditableCommandHandler<UpdateRolePermissionsCommand, int>
{
    private readonly IRoleRepository _roles;
    private readonly IPermissionChecker _permissions;
    private readonly IUnitOfWorkFactory _uowFactory;

    public UpdateRolePermissionsCommandHandler(IRoleRepository roles, IPermissionChecker permissions, IUnitOfWorkFactory uowFactory, IAuditRepository audit, ITenantContextAccessor tenantAccessor)
        : base(audit, tenantAccessor)
    {
        _roles = roles;
        _permissions = permissions;
        _uowFactory = uowFactory;
    }

    protected override async Task<int> HandleCore(UpdateRolePermissionsCommand cmd, CancellationToken ct)
    {
        await _permissions.EnsurePermission(cmd.UpdatedBy, Permission.UserManage, ct);

        var role = await _roles.GetById(cmd.RoleId, ct)
            ?? throw new InvalidOperationException($"Role {cmd.RoleId} not found.");
        role.UpdatePermissions(cmd.Permissions);

        await _roles.Save(role, ct);
        await using var uow = _uowFactory.Create();
        await uow.Commit(ct);
        return role.Version;
    }
}
