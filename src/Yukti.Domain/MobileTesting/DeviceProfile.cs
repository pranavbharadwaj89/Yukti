using Yukti.Domain.SharedKernel;

namespace Yukti.Domain.MobileTesting;

/// <summary>Mobile Testing Module Spec, domain model: Android | Ios.</summary>
public enum MobilePlatform { Android, Ios }

/// <summary>MOB-T3: how app state is handled between flows on the same device.
/// Full reinstalls the app (slowest, cleanest); Fast clears app data between flows
/// (the default); None reuses state deliberately, for flows that chain.</summary>
public enum ResetStrategy { Full, Fast, None }

/// <summary>MOB-T2: "a profile records whether its endpoint is local, self-hosted or a named
/// device farm, because error messages and retry behaviour differ." Metadata only for now --
/// MOB-T1's connection-failure classification doesn't yet branch on it; a later task can use it
/// to tailor retry/error behaviour per kind without changing this field's shape.</summary>
public enum DeviceEndpointKind { Local, SelfHosted, Farm }

/// <summary>
/// Mobile Testing Module Spec: "admin-configured, tenant-scoped. A flow references a
/// profile by name, never a raw capability blob — the same principle as DB connections."
///
/// MOB-T1 built this as a bare record — "the shape MOB-T1's session-creation layer needs
/// today," deliberately not yet persisted. MOB-T2 is this task: an <see cref="AggregateRoot{TId}"/>
/// with a visible <see cref="Version"/> counter, the same "disposable, editable resource" shape
/// <c>RequestTemplate</c> already uses (no publish step, no version-family branching — editing a
/// profile updates every flow that references it by name, with no flow edits, which is the whole
/// point of referencing by name rather than embedding a capability blob).
///
/// <see cref="FarmCredentialRef"/> is the spec's own "SecretRef?" field, under its real name:
/// no `SecretRef` type exists anywhere in this codebase. The actual, already-established
/// mechanism (Yukti.Contracts.ICredentialResolver, already used by GeminiProvider and proven
/// in FakeModules.cs's own FR-LOG-04 fixture) resolves a plain string reference to a secret
/// value, scoped per flow-run/per-module -- that reference string is what this field holds.
/// </summary>
public sealed class DeviceProfile : AggregateRoot<DeviceProfileId>
{
    public TenantId TenantId { get; private set; }
    public string Name { get; private set; }
    public MobilePlatform Platform { get; private set; }
    public string? PlatformVersion { get; private set; }
    public string? DeviceName { get; private set; }
    public string? App { get; private set; }
    public string AppiumUrl { get; private set; }
    public DeviceEndpointKind EndpointKind { get; private set; }
    public string? FarmCredentialRef { get; private set; }
    public ResetStrategy Reset { get; private set; }
    public TimeSpan SessionTimeout { get; private set; }
    public int Version { get; private set; }
    public IReadOnlyDictionary<string, object?> ExtraCapabilities { get; private set; }

    private DeviceProfile(DeviceProfileId id, TenantId tenantId, string name) : base(id)
    {
        TenantId = tenantId;
        Name = name;
        AppiumUrl = "http://localhost:4723";
        ExtraCapabilities = new Dictionary<string, object?>();
        Version = 1;
    }

    public static DeviceProfile Create(
        string name, TenantId tenantId, MobilePlatform platform, string? platformVersion, string? deviceName,
        string? app, string appiumUrl, DeviceEndpointKind endpointKind, string? farmCredentialRef,
        ResetStrategy reset = ResetStrategy.Fast, TimeSpan? sessionTimeout = null,
        IReadOnlyDictionary<string, object?>? extraCapabilities = null)
    {
        Validate(name, appiumUrl);

        return new DeviceProfile(DeviceProfileId.New(), tenantId, name)
        {
            Platform = platform,
            PlatformVersion = platformVersion,
            DeviceName = deviceName,
            App = app,
            AppiumUrl = appiumUrl,
            EndpointKind = endpointKind,
            FarmCredentialRef = farmCredentialRef,
            Reset = reset,
            SessionTimeout = sessionTimeout ?? TimeSpan.FromMinutes(5),
            ExtraCapabilities = extraCapabilities ?? new Dictionary<string, object?>(),
        };
    }

    public void Update(
        string name, MobilePlatform platform, string? platformVersion, string? deviceName, string? app,
        string appiumUrl, DeviceEndpointKind endpointKind, string? farmCredentialRef, ResetStrategy reset,
        TimeSpan sessionTimeout, IReadOnlyDictionary<string, object?>? extraCapabilities)
    {
        Validate(name, appiumUrl);

        Name = name;
        Platform = platform;
        PlatformVersion = platformVersion;
        DeviceName = deviceName;
        App = app;
        AppiumUrl = appiumUrl;
        EndpointKind = endpointKind;
        FarmCredentialRef = farmCredentialRef;
        Reset = reset;
        SessionTimeout = sessionTimeout;
        ExtraCapabilities = extraCapabilities ?? new Dictionary<string, object?>();
        Version++;
    }

    private static void Validate(string name, string appiumUrl)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("DeviceProfile name cannot be empty.");
        if (string.IsNullOrWhiteSpace(appiumUrl))
            throw new DomainException("DeviceProfile requires an Appium endpoint URL.");
    }
}
