using Yukti.Domain.SharedKernel;

namespace Yukti.Domain.MobileTesting;

/// <summary>Mobile Testing Module Spec, domain model: Android | Ios.</summary>
public enum MobilePlatform { Android, Ios }

/// <summary>MOB-T3: how app state is handled between flows on the same device.
/// Full reinstalls the app (slowest, cleanest); Fast clears app data between flows
/// (the default); None reuses state deliberately, for flows that chain.</summary>
public enum ResetStrategy { Full, Fast, None }

/// <summary>
/// Mobile Testing Module Spec: "admin-configured, tenant-scoped. A flow references a
/// profile by name, never a raw capability blob — the same principle as DB connections."
///
/// This record is the shape MOB-T1's session-creation layer needs today. MOB-T2 is where
/// it actually becomes admin-configured/persisted/tenant-scoped/versioned/looked-up-by-name
/// (its own repository, summary query, "test profile" admin action, publish validation) —
/// deliberately not built here, so MOB-T1 stays scoped to the WebDriver client itself. This
/// is the same shape either way, so MOB-T2 has nothing to redesign, only to persist.
///
/// <see cref="FarmCredentialRef"/> is the spec's own "SecretRef?" field, under its real name:
/// no `SecretRef` type exists anywhere in this codebase. The actual, already-established
/// mechanism (Yukti.Contracts.ICredentialResolver, already used by GeminiProvider and proven
/// in FakeModules.cs's own FR-LOG-04 fixture) resolves a plain string reference to a secret
/// value, scoped per flow-run/per-module -- that reference string is what this field holds.
/// </summary>
public sealed record DeviceProfile(
    DeviceProfileId Id,
    string Name,
    MobilePlatform Platform,
    string? PlatformVersion,
    string? DeviceName,
    string? App,
    string AppiumUrl,
    string? FarmCredentialRef,
    ResetStrategy Reset,
    TimeSpan SessionTimeout,
    TenantId TenantId,
    IReadOnlyDictionary<string, object?>? ExtraCapabilities);
