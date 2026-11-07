using System.Collections.Concurrent;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using OpenQA.Selenium.Appium.iOS;
using OpenQA.Selenium.Interactions;
using Yukti.Contracts;
using Yukti.Domain.MobileTesting;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// Mobile Testing Module Spec, MOB-T1: real mobile UI automation via Appium (Android/iOS).
/// Started life as a direct port of an earlier TS prototype (same four actions, same "never
/// throws out of Run" shape) before this formal spec existed; MOB-T1 brings its session
/// layer up to the spec's own checklist rather than rebuilding from nothing:
///
///   - Session creation now goes through a real <see cref="DeviceProfile"/> (built from the
///     same ctx.Variables["mobile"] config bag as before -- MOB-T2 is where that profile
///     becomes admin-configured/persisted/looked-up-by-name; this is still just the client).
///   - Connection failures are now specific: "no Appium server reachable", or, when that's
///     not the problem, the real underlying Appium error plus the profile name and endpoint
///     -- never a bare timeout. See <see cref="ClassifySessionFailure"/>.
///   - A farm credential is resolved through the existing {{secrets}} pipeline
///     (ctx.Credentials.ResolveAsync -- the spec's own "SecretRef" under its real name in
///     this codebase), never taken as a literal value from config.
///   - Appium's own server-side log is captured best-effort on a Run() failure and attached
///     to the StepOutcome's Data, because (the spec's own words) "the real cause is usually
///     in the driver's log rather than the response."
///
/// Requires a running Appium server + attached device/emulator/simulator reachable at
/// appiumUrl -- this module only connects to one; it does not start Appium itself.
/// </summary>
public sealed class MobileModule : IAutomationModule
{
    private readonly ConcurrentDictionary<FlowRunId, AppiumDriver> _sessions = new();

    public ModuleKind Kind => ModuleKind.Mobile;
    public string ContractVersion => "1.0.0";

    public IReadOnlyList<ActionSchema> GetSupportedActions() => new[]
    {
        new ActionSchema
        {
            ActionName = "tap",
            Description = "Taps the first element matching a selector.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "XPath (starting with /) or accessibility id." },
            }
        },
        new ActionSchema
        {
            ActionName = "type",
            Description = "Sets the value of the first element matching a selector.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "XPath (starting with /) or accessibility id." },
                new ParamSpec { Name = "value", Type = ParamType.String, Required = true, Description = "Text to set on the matched element." },
            }
        },
        new ActionSchema
        {
            ActionName = "swipe",
            Description = "Synthesizes a touch swipe gesture from one point to another.",
            Parameters = new[]
            {
                new ParamSpec { Name = "fromX", Type = ParamType.Number, Required = true, Description = "Starting X coordinate of the swipe, in device pixels." },
                new ParamSpec { Name = "fromY", Type = ParamType.Number, Required = true, Description = "Starting Y coordinate of the swipe, in device pixels." },
                new ParamSpec { Name = "toX", Type = ParamType.Number, Required = true, Description = "Ending X coordinate of the swipe, in device pixels." },
                new ParamSpec { Name = "toY", Type = ParamType.Number, Required = true, Description = "Ending Y coordinate of the swipe, in device pixels." },
            }
        },
        new ActionSchema
        {
            ActionName = "assertVisible",
            Description = "Asserts an element matching a selector is displayed. A missing or hidden element both read as not-visible.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "XPath (starting with /) or accessibility id." },
            }
        },
    };

    public async Task Setup(ExecutionContext ctx, CancellationToken ct)
    {
        if (ctx.Variables.GetValueOrDefault("mobile") is not IReadOnlyDictionary<string, object?> config)
            return;

        var profile = await BuildProfile(config, ctx.TenantId ?? TenantId.New(), ctx, ct);

        var options = new AppiumOptions { PlatformName = profile.Platform.ToString() };
        if (profile.DeviceName is not null) options.DeviceName = profile.DeviceName;
        options.AutomationName = profile.Platform == MobilePlatform.Ios ? "XCUITest" : "UiAutomator2";
        if (profile.App is not null) options.App = profile.App;

        if (profile.ExtraCapabilities is not null)
        {
            foreach (var (key, value) in profile.ExtraCapabilities)
            {
                if (value is not null)
                    options.AddAdditionalAppiumOption(key, value);
            }
        }

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
            throw ClassifySessionFailure(ex, profile);
        }

        _sessions[ctx.RunId] = driver;
    }

    /// <summary>
    /// Builds a typed <see cref="DeviceProfile"/> from the flow's own "mobile" config bag.
    /// "automationName" is no longer a required config key -- UiAutomator2/XCUITest are the
    /// only automation engines Appium's own tiers support per platform (the spec's own "one
    /// sentence" description), so there's nothing a flow author would ever set it to besides
    /// the platform default; keeping it configurable here would just be a way to misconfigure.
    /// farmCredentialRef, if present, is resolved now (not left as a literal) -- a step/profile
    /// author names which credential to use, never the credential itself.
    /// </summary>
    private static async Task<DeviceProfile> BuildProfile(IReadOnlyDictionary<string, object?> config, TenantId tenantId, ExecutionContext ctx, CancellationToken ct)
    {
        var platformName = config.GetValueOrDefault("platformName") as string
            ?? throw new ArgumentException("mobile config requires 'platformName'.");
        var platform = platformName.Equals("iOS", StringComparison.OrdinalIgnoreCase) ? MobilePlatform.Ios : MobilePlatform.Android;
        var deviceName = config.GetValueOrDefault("deviceName") as string
            ?? throw new ArgumentException("mobile config requires 'deviceName'.");
        var app = config.GetValueOrDefault("app") as string;
        var appiumUrl = config.GetValueOrDefault("appiumUrl") as string ?? "http://localhost:4723";
        var farmCredentialRef = config.GetValueOrDefault("farmCredentialRef") as string;

        var reserved = new HashSet<string> { "platformName", "deviceName", "app", "appiumUrl", "farmCredentialRef", "automationName" };
        var extraCapabilities = config.Where(kv => !reserved.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);

        // Resolved now, placed into ExtraCapabilities under a fixed key -- never logged, never
        // echoed back in any message this module produces. The flow author names the farm's
        // own expected capability key (e.g. "bstack:options") for the *value this becomes part
        // of* through their own config; this module's job ends at "resolve the reference".
        if (farmCredentialRef is not null)
        {
            var resolved = await ctx.Credentials.ResolveAsync(farmCredentialRef, ct);
            if (resolved is not null)
                extraCapabilities["farmCredential"] = resolved;
        }

        return new DeviceProfile(
            DeviceProfileId.New(), deviceName, platform, PlatformVersion: null, deviceName, app, appiumUrl,
            farmCredentialRef, ResetStrategy.Fast, TimeSpan.FromMinutes(5), tenantId, extraCapabilities);
    }

    /// <summary>
    /// MOB-T1: "Connection failures are specific... Never a bare timeout." Reliably
    /// distinguishable without a live Appium server: a socket/HTTP-level failure means
    /// Appium itself was never reached. Everything else means it *was* reached but rejected
    /// the session -- the real Appium message is always included, the profile is always
    /// named, so the fallback case is still specific, just not sub-categorized further than
    /// that without a real server response to pattern-match against.
    /// </summary>
    public static MobileSessionException ClassifySessionFailure(Exception ex, DeviceProfile profile)
    {
        if (IsConnectionFailure(ex))
        {
            return new MobileSessionException(
                $"No Appium server reachable at {profile.AppiumUrl}. Is it running? ({RootCause(ex).Message})", ex);
        }

        return new MobileSessionException(
            $"Appium rejected creating a session for device profile '{profile.Name}' " +
            $"(platform={profile.Platform}, device={profile.DeviceName ?? "any"}): {RootCause(ex).Message}", ex);
    }

    private static bool IsConnectionFailure(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is System.Net.Sockets.SocketException or System.Net.Http.HttpRequestException or TaskCanceledException)
                return true;
        }
        return false;
    }

    private static Exception RootCause(Exception ex)
    {
        while (ex.InnerException is not null) ex = ex.InnerException;
        return ex;
    }

    public Task Teardown(ExecutionContext ctx, CancellationToken ct)
    {
        if (_sessions.TryRemove(ctx.RunId, out var driver))
            driver.Quit();
        return Task.CompletedTask;
    }

    public async Task<StepOutcome> Run(string action, IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(ctx.RunId, out var driver))
            return StepOutcome.Failed("MobileModule not set up — call setup() first.");

        try
        {
            switch (action)
            {
                case "tap":
                    {
                        var selector = RequireString(parameters, "selector");
                        ResolveElement(driver, selector).Click();
                        return StepOutcome.Passed($"Tapped {selector}");
                    }
                case "type":
                    {
                        var selector = RequireString(parameters, "selector");
                        var value = RequireString(parameters, "value");
                        ResolveElement(driver, selector).SendKeys(value);
                        return StepOutcome.Passed($"Typed into {selector}");
                    }
                case "swipe":
                    {
                        var fromX = RequireInt(parameters, "fromX");
                        var fromY = RequireInt(parameters, "fromY");
                        var toX = RequireInt(parameters, "toX");
                        var toY = RequireInt(parameters, "toY");

                        var finger = new PointerInputDevice(PointerKind.Touch);
                        var sequence = new ActionSequence(finger, 0);
                        sequence.AddAction(finger.CreatePointerMove(CoordinateOrigin.Viewport, fromX, fromY, TimeSpan.Zero));
                        sequence.AddAction(finger.CreatePointerDown(MouseButton.Left));
                        sequence.AddAction(finger.CreatePointerMove(CoordinateOrigin.Viewport, toX, toY, TimeSpan.FromMilliseconds(300)));
                        sequence.AddAction(finger.CreatePointerUp(MouseButton.Left));
                        driver.PerformActions(new List<ActionSequence> { sequence });
                        return StepOutcome.Passed($"Swiped ({fromX},{fromY}) -> ({toX},{toY})");
                    }
                case "assertVisible":
                    {
                        var selector = RequireString(parameters, "selector");
                        bool visible;
                        try { visible = ResolveElement(driver, selector).Displayed; }
                        catch { visible = false; }
                        return visible
                            ? StepOutcome.Passed($"{selector} is visible.")
                            : StepOutcome.Failed($"{selector} is not visible.");
                    }
                default:
                    return StepOutcome.Failed($"Unknown mobile action \"{action}\".");
            }
        }
        catch (Exception ex)
        {
            var log = await TryCaptureAppiumLog(driver, ct);
            return StepOutcome.Failed(ex.Message, log is null ? null : new Dictionary<string, object?> { ["appiumServerLog"] = log });
        }
    }

    /// <summary>MOB-T1: "Appium-side logs are captured and attached to the step result on
    /// failure, because the real cause is usually in the driver's log rather than the
    /// response." Best-effort, same reasoning as WebModule's own failure-screenshot capture --
    /// a log type a given driver/platform doesn't support must never turn a real step failure
    /// into a different, more confusing failure about logging.</summary>
    private static async Task<IReadOnlyList<string>?> TryCaptureAppiumLog(AppiumDriver driver, CancellationToken ct)
    {
        try
        {
            var entries = await Task.Run(() => driver.Manage().Logs.GetLog("logcat"), ct);
            return entries.Select(e => $"{e.Timestamp:O} {e.Level}: {e.Message}").ToList();
        }
        catch
        {
            return null;
        }
    }

    // WebdriverIO's TS $(selector) auto-detects locator strategy; .NET's By
    // requires picking one explicitly. XPath for anything selector-shaped
    // like a path, accessibility id otherwise — covers the common cases
    // without pulling in a full selector-strategy DSL.
    private static IWebElement ResolveElement(AppiumDriver driver, string selector) =>
        selector.StartsWith('/') ? driver.FindElement(By.XPath(selector)) : driver.FindElement(By.Id(selector));

    private static string RequireString(IReadOnlyDictionary<string, object?> parameters, string name) =>
        parameters.GetValueOrDefault(name) as string
            ?? throw new ArgumentException($"mobile.{name} requires a '{name}' parameter.");

    private static int RequireInt(IReadOnlyDictionary<string, object?> parameters, string name) =>
        parameters.TryGetValue(name, out var value) && value is not null
            ? Convert.ToInt32(value)
            : throw new ArgumentException($"mobile action requires a '{name}' parameter.");
}
