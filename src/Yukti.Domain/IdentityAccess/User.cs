using Yukti.Domain.Events;
using Yukti.Domain.SharedKernel;

namespace Yukti.Domain.IdentityAccess;

/// <summary>
/// Aggregate root of the Identity &amp; Access context. RBAC (Role,
/// Permission, RoleVersion) landed per Volume 1 Part IV §25 — a User holds
/// zero or more RoleId assignments; the permission set itself lives on the
/// Role aggregate, never duplicated here (§25.1-25.2).
/// PasswordHash is opaque to the domain — hashing is Infrastructure's job
/// (IPasswordHasher, Yukti.Application.Abstractions); this aggregate only
/// stores and swaps the resulting hash, never a plaintext password.
/// </summary>
public sealed class User : AggregateRoot<UserId>
{
    public string Email { get; }
    public string DisplayName { get; }
    public TenantId TenantId { get; }
    public bool IsServiceAccount { get; }
    public string PasswordHash { get; private set; }

    private readonly List<RoleId> _roleIds = new();
    public IReadOnlyList<RoleId> RoleIds => _roleIds.AsReadOnly();

    private User(UserId id, string email, string displayName, TenantId tenantId, string passwordHash, bool isServiceAccount)
        : base(id)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new DomainException("User email cannot be empty.");

        Email = email;
        DisplayName = displayName;
        TenantId = tenantId;
        PasswordHash = passwordHash;
        IsServiceAccount = isServiceAccount;
    }

    public static User Register(string email, string displayName, TenantId tenantId, string passwordHash, bool isServiceAccount = false)
    {
        var user = new User(UserId.New(), email, displayName, tenantId, passwordHash, isServiceAccount);
        user.RaiseDomainEvent(new UserRegisteredEvent(user.Id, email, tenantId, DateTimeOffset.UtcNow));
        return user;
    }

    public void AssignRole(RoleId roleId)
    {
        if (_roleIds.Contains(roleId)) return;
        _roleIds.Add(roleId);
        RaiseDomainEvent(new UserRoleAssignedEvent(Id, roleId, DateTimeOffset.UtcNow));
    }

    public void RevokeRole(RoleId roleId) => _roleIds.Remove(roleId);
}
