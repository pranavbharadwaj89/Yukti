using Yukti.Domain.Events;
using Yukti.Domain.SharedKernel;

namespace Yukti.Domain.IdentityAccess;

/// <summary>
/// Aggregate root of the Identity &amp; Access context's role model.
/// Invariants:
///  - Permissions can only change via UpdatePermissions, which always
///    increments Version — the JWT's RoleVersion claim (FR-AUTH-03) exists
///    to let a caller detect staleness, but the authoritative check
///    (PermissionChecker, Yukti.Application) never trusts the claim itself;
///    it always re-reads this aggregate's live Permissions from the
///    repository, so a revoked permission takes effect on the very next
///    request — a stronger guarantee than FR-AUTHZ-04 requires, not a
///    weaker one.
///  - TenantId null means a global baseline role (Administrator, Flow
///    Author, Flow Runner per §25.1-25.2); non-null scopes a custom role to
///    one tenant.
/// (Volume 1 Part IV §25.1-25.2, §24.6)
/// </summary>
public sealed class Role : AggregateRoot<RoleId>
{
    public string Name { get; private set; }
    public TenantId? TenantId { get; }
    public int Version { get; private set; }

    private readonly HashSet<Permission> _permissions;
    public IReadOnlySet<Permission> Permissions => _permissions;

    private Role(RoleId id, string name, TenantId? tenantId, IEnumerable<Permission> permissions) : base(id)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Role name cannot be empty.");

        Name = name;
        TenantId = tenantId;
        Version = 1;
        _permissions = new HashSet<Permission>(permissions);
    }

    // EF Core materialization only. The business constructor above takes
    // IEnumerable<Permission>, but the mapped Permissions property is
    // IReadOnlySet<Permission> — close enough for a human, not an exact
    // type match for EF Core's constructor-parameter binding, which
    // requires the parameter type to line up with the property it binds
    // to. This overload also takes the already-materialized Version rather
    // than always resetting it to 1, so reloading a role whose permissions
    // were updated (Version bumped via UpdatePermissions) round-trips
    // correctly instead of silently reverting to Version 1 on every load.
    private Role(RoleId id, string name, TenantId? tenantId, int version, IReadOnlySet<Permission> permissions) : base(id)
    {
        Name = name;
        TenantId = tenantId;
        Version = version;
        _permissions = new HashSet<Permission>(permissions);
    }

    public static Role Create(string name, TenantId? tenantId, IEnumerable<Permission> permissions)
    {
        var role = new Role(RoleId.New(), name, tenantId, permissions);
        role.RaiseDomainEvent(new RoleCreatedEvent(role.Id, name, tenantId, role.Version, DateTimeOffset.UtcNow));
        return role;
    }

    /// <summary>Baseline roles per §25.1-25.2. Flow Runner's set is pinned exactly by FR-AUTHZ-01's acceptance criterion.</summary>
    public static Role CreateBaselineAdministrator() =>
        Create("Administrator", tenantId: null, Enum.GetValues<Permission>());

    public static Role CreateBaselineFlowAuthor() =>
        Create("Flow Author", tenantId: null, new[]
        {
            Permission.FlowCreate, Permission.FlowEdit, Permission.FlowPublish,
            Permission.FlowView, Permission.ReportView,
        });

    public static Role CreateBaselineFlowRunner() =>
        Create("Flow Runner", tenantId: null, new[] { Permission.FlowExecute, Permission.ReportView });

    public void UpdatePermissions(IEnumerable<Permission> permissions)
    {
        _permissions.Clear();
        foreach (var p in permissions) _permissions.Add(p);
        Version++;
        RaiseDomainEvent(new RolePermissionsUpdatedEvent(Id, Version, DateTimeOffset.UtcNow));
    }

    public bool Has(Permission permission) => _permissions.Contains(permission);
}
