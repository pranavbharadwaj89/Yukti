using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using OpenQA.Selenium.Appium.Android.Enums;
using Yukti.Domain.MobileTesting;

namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// Mobile Testing Module Spec, MOB-T6: every <see cref="DeviceCommand"/>. Split the same way
/// MOB-T4/T5's own executors split pure decisions from driver calls: <see cref="IsSupported"/> --
/// "which commands this client can issue on which platform, and why not otherwise" -- is pure,
/// directly unit-testable, and is the real answer to the spec's own "commands unsupported on a
/// platform fail clearly... a silent no-op makes a test pass while proving nothing." Every
/// <c>ExecuteAsync</c>-reachable call needs a live Appium server/device this environment doesn't
/// have, the same standing constraint as every prior Mobile task.
///
/// Every execution path below uses either a real typed <c>Appium.Net</c> client method
/// (confirmed against the installed 8.2.0 package's own XML docs before writing this, not
/// guessed) or Appium's own documented <c>mobile: xxx</c> script-execution extension mechanism
/// (<c>mobile: setConnectivity</c>, <c>mobile: pressButton</c>, <c>mobile: acceptAlert</c>/
/// <c>mobile: dismissAlert</c> -- all stable, officially documented Appium driver extensions,
/// the same bridge any Appium client uses for a capability with no dedicated typed wrapper).
/// </summary>
public static class AppiumDeviceCommandExecutor
{
    /// <summary>Pure: the actual decision behind every "not supported on this platform" failure.
    /// No driver needed -- directly unit-testable for every (command, platform) pair the spec's
    /// own checklist cares about.</summary>
    public static (bool Supported, string? Reason) IsSupported(DeviceCommand command, MobilePlatform platform) => command switch
    {
        Yukti.Domain.MobileTesting.ToggleWifi or Yukti.Domain.MobileTesting.ToggleAirplaneMode => platform == MobilePlatform.Android
            ? (true, null)
            : (false, "Toggling Wi-Fi/airplane mode is not supported on iOS -- Appium's XCUITest " +
                      "driver has no equivalent to UiAutomator2's \"mobile: setConnectivity\", and a " +
                      "real device's radios aren't controllable this way without MDM."),

        PressDeviceKey(DeviceKey.Back) => platform == MobilePlatform.Android
            ? (true, null)
            : (false, "iOS has no hardware Back button; there is nothing for this command to press."),

        PressDeviceKey(DeviceKey.Enter) => platform == MobilePlatform.Android
            ? (true, null)
            : (false, "iOS has no hardware Enter key; use the Web/Mobile module's own text-input " +
                      "actions for a software keyboard's return key instead."),

        PressDeviceKey(DeviceKey.Home) => (true, null), // Android: PressKeyCode; iOS: "mobile: pressButton".

        Yukti.Domain.MobileTesting.OpenNotifications => platform == MobilePlatform.Android
            ? (true, null)
            : (false, "iOS has no system notification shade Appium can open this way."),

        // Rotate, HideKeyboard, BackgroundApp, HandlePermissionDialog, InstallApp, RemoveApp,
        // ResetApp, SwitchContext: every one of these has a real, confirmed, cross-platform
        // Appium.Net 8.2.0 call (AppiumDriver base class or a documented mobile: extension both
        // drivers implement) -- supported on both platforms.
        _ => (true, null),
    };

    public static void Rotate(AppiumDriver driver, Orientation to) =>
        driver.Orientation = to == Orientation.Landscape ? ScreenOrientation.Landscape : ScreenOrientation.Portrait;

    /// <summary>Android-only (see <see cref="IsSupported"/>) -- "mobile: setConnectivity" is a
    /// UiAutomator2-specific extension; the caller must check <see cref="IsSupported"/> first.</summary>
    public static void ToggleWifi(AppiumDriver driver, bool on) =>
        driver.ExecuteScript("mobile: setConnectivity", new Dictionary<string, object> { ["wifi"] = on });

    public static void ToggleAirplaneMode(AppiumDriver driver, bool on) =>
        driver.ExecuteScript("mobile: setConnectivity", new Dictionary<string, object> { ["airplaneMode"] = on });

    public static void PressDeviceKey(AppiumDriver driver, DeviceKey key, MobilePlatform platform)
    {
        if (platform == MobilePlatform.Android)
        {
            if (driver is not AndroidDriver android)
                throw new InvalidOperationException("PressDeviceKey(Android) requires an AndroidDriver session.");
            var keyCode = key switch
            {
                DeviceKey.Back => AndroidKeyCode.Keycode_BACK,
                DeviceKey.Home => AndroidKeyCode.Keycode_HOME,
                DeviceKey.Enter => AndroidKeyCode.Keycode_ENTER,
                _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unrecognized device key."),
            };
            android.PressKeyCode(new KeyEvent((int)keyCode));
            return;
        }

        // iOS: only Home is reachable here -- IsSupported already rejects Back/Enter.
        driver.ExecuteScript("mobile: pressButton", new Dictionary<string, object> { ["name"] = "home" });
    }

    public static void HideKeyboard(AppiumDriver driver) => driver.HideKeyboard();

    public static void OpenNotifications(AppiumDriver driver)
    {
        if (driver is not AndroidDriver android)
            throw new InvalidOperationException("OpenNotifications requires an AndroidDriver session.");
        android.OpenNotifications();
    }

    public static void BackgroundApp(AppiumDriver driver, TimeSpan duration) => driver.BackgroundApp(duration);

    public static void HandlePermissionDialog(AppiumDriver driver, PermissionAction action) =>
        driver.ExecuteScript(action == PermissionAction.Allow ? "mobile: acceptAlert" : "mobile: dismissAlert", new Dictionary<string, object>());

    public static void InstallApp(AppiumDriver driver, string location) => driver.InstallApp(location);

    public static void RemoveApp(AppiumDriver driver, string bundleId) => driver.RemoveApp(bundleId);

    public static void ResetApp(AppiumDriver driver, string appId)
    {
        driver.TerminateApp(appId);
        driver.ActivateApp(appId);
    }

    public static void SwitchContext(AppiumDriver driver, MobileContext context)
    {
        if (context == MobileContext.Native)
        {
            driver.Context = "NATIVE_APP";
            return;
        }

        var webview = driver.Contexts.FirstOrDefault(c => !c.Equals("NATIVE_APP", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("No webview context is available on this session.");
        driver.Context = webview;
    }
}
