using Yukti.Domain.MobileTesting;

namespace Yukti.Application.Abstractions;

/// <summary>Mobile Testing Module Spec, MOB-T2: "Admin UI with a 'test profile' action that opens
/// a session, takes a screenshot and tears down, so a misconfiguration is caught at setup rather
/// than in a failing test run." A port, not a direct Appium dependency here — Yukti.Application
/// never references Infrastructure (same reasoning IModuleActionResolver's own doc comment
/// states); the real implementation (AppiumDeviceProfileTester, Yukti.Infrastructure.InMemory)
/// reuses MobileModule.ClassifySessionFailure for the same specific-not-bare-timeout failure
/// shape MOB-T1 already established.</summary>
public sealed record DeviceProfileTestResult(bool Success, string? Error, byte[]? Screenshot);

public interface IDeviceProfileTester
{
    Task<DeviceProfileTestResult> TestConnection(DeviceProfile profile, CancellationToken ct);
}
