using Yukti.Domain.SharedKernel;

namespace Yukti.Domain.IdentityAccess;

/// <summary>
/// Minimal for this session's backend-core scope (Volume 1 Part II §10.6).
/// Full RBAC (Role, Permission enum, RoleVersion) is Part IV, Section 25's
/// scope — a later pass once this core is in place.
/// </summary>
public sealed class User : Entity<UserId>
{
    public string Email { get; }
    public string DisplayName { get; }
    public TenantId TenantId { get; }
    public bool IsServiceAccount { get; }

    public User(UserId id, string email, string displayName, TenantId tenantId, bool isServiceAccount = false)
        : base(id)
    {
        Email = email;
        DisplayName = displayName;
        TenantId = tenantId;
        IsServiceAccount = isServiceAccount;
    }
}
