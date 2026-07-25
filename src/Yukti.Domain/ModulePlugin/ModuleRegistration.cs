using Yukti.Domain.Events;
using Yukti.Domain.SharedKernel;

namespace Yukti.Domain.ModulePlugin;

/// <summary>Thin entity wrapping an ActionSchema with registration-specific metadata. (Volume 1 Part II §10.5)</summary>
public sealed class ModuleActionEntry : Entity<ModuleActionEntryId>
{
    public string ActionName { get; }
    public ActionSchema Schema { get; }
    public bool IsDeprecated { get; private set; }
    public string? DeprecationNotice { get; private set; }

    public ModuleActionEntry(string actionName, ActionSchema schema) : base(ModuleActionEntryId.New())
    {
        ActionName = actionName;
        Schema = schema;
    }

    public void Deprecate(string notice)
    {
        IsDeprecated = true;
        DeprecationNotice = notice;
    }
}

/// <summary>
/// Aggregate root of the Module &amp; Plugin context. Invariants:
///  - Kind must be unique among currently-active registrations within a
///    tenant's accessible module set.
///  - Trust tier is immutable after registration; a re-certification
///    creates a new version rather than mutating trust tier in place.
/// (Volume 1 Part II §9.4)
/// </summary>
public sealed class ModuleRegistration : AggregateRoot<ModuleRegistrationId>
{
    public ModuleKind Kind { get; }
    public string DisplayName { get; }
    public TrustTier Trust { get; }
    public string ContractVersion { get; }
    public bool IsActive { get; private set; } = true;
    public TenantId? TenantId { get; } // null = global (BuiltIn/Verified); populated for tenant-scoped Community installs

    private readonly List<ModuleActionEntry> _actions = new();
    public IReadOnlyList<ModuleActionEntry> Actions => _actions.AsReadOnly();

    public ModuleRegistration(ModuleKind kind, string displayName, TrustTier trust, string contractVersion, TenantId? tenantId = null)
        : base(ModuleRegistrationId.New())
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new DomainException("ModuleRegistration display name cannot be empty.");

        Kind = kind;
        DisplayName = displayName;
        Trust = trust;
        ContractVersion = contractVersion;
        TenantId = tenantId;

        RaiseDomainEvent(new ModuleRegisteredEvent(Id, Kind, Trust, DateTimeOffset.UtcNow));
    }

    public void RegisterAction(ActionSchema schema) => _actions.Add(new ModuleActionEntry(schema.ActionName, schema));

    public bool HasAction(string actionName) => _actions.Any(a => a.ActionName == actionName && !a.IsDeprecated);

    public void DeprecateAction(string actionName, string notice)
    {
        var entry = _actions.FirstOrDefault(a => a.ActionName == actionName)
            ?? throw new DomainException($"Action '{actionName}' is not registered on module '{Kind}'.");
        entry.Deprecate(notice);
        RaiseDomainEvent(new ModuleDeprecatedActionEvent(Id, actionName, notice, DateTimeOffset.UtcNow));
    }

    public void Deactivate() => IsActive = false;
}
