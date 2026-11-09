using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using OpenQA.Selenium.Appium.iOS;
using Yukti.Application.Abstractions;
using Yukti.Domain.MobileTesting;
using Yukti.Infrastructure.InMemory.Modules;

namespace Yukti.Infrastructure.InMemory;

/// <summary>
/// Mobile Testing Module Spec, MOB-T2: "Admin UI with a 'test profile' action that opens a
/// session, takes a screenshot and tears down, so a misconfiguration is caught at setup rather
/// than in a failing test run." Deliberately standalone from MobileModule rather than routed
/// through it — this has no FlowRunId, no flow, no step; it is a one-shot admin action against a
/// persisted DeviceProfile, not a flow-run session. Reuses MobileModule.ClassifySessionFailure
/// (public, for exactly this kind of direct reuse) so a misconfigured profile gets the same
/// specific "no Appium server reachable at ..." / real-error-plus-profile-name message MOB-T1
/// already established for flow runs, not a second, differently-worded failure shape.
/// </summary>
public sealed class AppiumDeviceProfileTester : IDeviceProfileTester
{
    public async Task<DeviceProfileTestResult> TestConnection(DeviceProfile profile, CancellationToken ct)
    {
        var options = new AppiumOptions { PlatformName = profile.Platform.ToString() };
        if (profile.DeviceName is not null) options.DeviceName = profile.DeviceName;
        options.AutomationName = profile.Platform == MobilePlatform.Ios ? "XCUITest" : "UiAutomator2";
        if (profile.App is not null) options.App = profile.App;

        foreach (var (key, value) in profile.ExtraCapabilities)
            if (value is not null)
                options.AddAdditionalAppiumOption(key, value);

        var remoteAddress = new Uri(profile.AppiumUrl);

        AppiumDriver driver;
        try
        {
            driver = profile.Platform == MobilePlatform.Ios
                ? new IOSDriver(remoteAddress, options)
                : new AndroidDriver(remoteAddress, options);
        }
        catch (Exception ex)
        {
            var classified = MobileModule.ClassifySessionFailure(ex, profile);
            return new DeviceProfileTestResult(Success: false, classified.Message, Screenshot: null);
        }

        try
        {
            var screenshot = await Task.Run(() => driver.GetScreenshot(), ct);
            return new DeviceProfileTestResult(Success: true, Error: null, screenshot.AsByteArray);
        }
        catch (Exception ex)
        {
            return new DeviceProfileTestResult(Success: false, ex.Message, Screenshot: null);
        }
        finally
        {
            try { driver.Quit(); } catch { /* best-effort teardown, same tolerance MobileModule.Teardown already has */ }
        }
    }
}
