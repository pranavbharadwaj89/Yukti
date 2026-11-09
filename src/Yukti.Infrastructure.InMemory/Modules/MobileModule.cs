using System.Collections.Concurrent;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using OpenQA.Selenium.Appium.iOS;
using Yukti.Contracts;
using Yukti.Domain.Execution;
using Yukti.Domain.MobileTesting;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Domain.UiVocabulary;
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
    /// <summary>MOB-T3: a session's own creation time + its profile's SessionTimeout, so Run()
    /// can tell a stale session from a healthy one without a timer -- checked lazily, on the
    /// next action against it, same "no background machinery" shape RetryFlakeHandler's own
    /// per-call checks already use.</summary>
    private sealed record MobileSession(AppiumDriver Driver, MobilePlatform Platform, DateTimeOffset CreatedAt, TimeSpan Timeout)
    {
        public bool IsExpired(DateTimeOffset now) => now - CreatedAt > Timeout;
    }

    /// <summary>MOB-T4, mirrors WebModule's own DefaultLocatorTimeoutMs — used only for the
    /// Locator-based resolution path, never the legacy plain-string one, which keeps its own
    /// implicit per-FindElement behaviour unchanged (zero behavioural change for an existing
    /// flow's plain-string selector).</summary>
    private static readonly TimeSpan DefaultLocatorTimeout = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<FlowRunId, MobileSession> _sessions = new();
    private readonly TimeProvider _clock;

    public MobileModule(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

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
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = MobileSelectorParamDescription },
            }
        },
        new ActionSchema
        {
            ActionName = "type",
            Description = "Sets the value of the first element matching a selector.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = MobileSelectorParamDescription },
                new ParamSpec { Name = "value", Type = ParamType.String, Required = true, Description = "Text to set on the matched element." },
            }
        },
        new ActionSchema
        {
            ActionName = "swipe",
            Description = "Synthesizes a touch swipe gesture. Prefer direction + distance (screen-size " +
                "independent); fall back to the explicit fromX/fromY/toX/toY pixel coordinates only for " +
                "a swipe a direction can't express.",
            Parameters = new[]
            {
                new ParamSpec { Name = "direction", Type = ParamType.String, Required = false, Description = "Up, Down, Left, or Right. Mutually exclusive with fromX/fromY/toX/toY." },
                new ParamSpec { Name = "distance", Type = ParamType.Number, Required = false, DefaultValue = 0.5, Description = "Fraction (0-1) of the scoped region's own dimension to swipe, centered. Default 0.5." },
                new ParamSpec { Name = "within", Type = ParamType.String, Required = false, Description = "Optional selector scoping the swipe to an element's bounds instead of the whole screen. " + MobileSelectorParamDescription },
                new ParamSpec { Name = "fromX", Type = ParamType.Number, Required = false, Description = "Legacy: starting X coordinate of the swipe, in device pixels." },
                new ParamSpec { Name = "fromY", Type = ParamType.Number, Required = false, Description = "Legacy: starting Y coordinate of the swipe, in device pixels." },
                new ParamSpec { Name = "toX", Type = ParamType.Number, Required = false, Description = "Legacy: ending X coordinate of the swipe, in device pixels." },
                new ParamSpec { Name = "toY", Type = ParamType.Number, Required = false, Description = "Legacy: ending Y coordinate of the swipe, in device pixels." },
            }
        },
        new ActionSchema
        {
            ActionName = "swipeBetween",
            Description = "Synthesizes a touch swipe between two explicit points, for a swipe no direction " +
                "can express. Points are fractions (0-1) of the scoped region, not pixels, so a flow isn't " +
                "tied to one screen size.",
            Parameters = new[]
            {
                new ParamSpec { Name = "fromX", Type = ParamType.Number, Required = true, Description = "Starting X, a fraction (0-1) of the scoped region's width." },
                new ParamSpec { Name = "fromY", Type = ParamType.Number, Required = true, Description = "Starting Y, a fraction (0-1) of the scoped region's height." },
                new ParamSpec { Name = "toX", Type = ParamType.Number, Required = true, Description = "Ending X, a fraction (0-1) of the scoped region's width." },
                new ParamSpec { Name = "toY", Type = ParamType.Number, Required = true, Description = "Ending Y, a fraction (0-1) of the scoped region's height." },
                new ParamSpec { Name = "within", Type = ParamType.String, Required = false, Description = "Optional selector scoping the two points to an element's bounds instead of the whole screen. " + MobileSelectorParamDescription },
                new ParamSpec { Name = "durationMs", Type = ParamType.Number, Required = false, DefaultValue = 300, Description = "How long the swipe takes, in milliseconds. Default 300." },
            }
        },
        new ActionSchema
        {
            ActionName = "doubleTap",
            Description = "Double-taps the first element matching a selector.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = MobileSelectorParamDescription },
            }
        },
        new ActionSchema
        {
            ActionName = "longPress",
            Description = "Presses and holds the first element matching a selector.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = MobileSelectorParamDescription },
                new ParamSpec { Name = "durationMs", Type = ParamType.Number, Required = false, DefaultValue = 1000, Description = "How long to hold, in milliseconds. Default 1000." },
            }
        },
        new ActionSchema
        {
            ActionName = "scrollTo",
            Description = "Scrolls until an element matching a selector becomes visible, up to a bounded " +
                "number of attempts -- an element that never appears fails in seconds, not forever.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = MobileSelectorParamDescription },
                new ParamSpec { Name = "container", Type = ParamType.String, Required = false, Description = "Optional selector scoping the scroll gesture to a scrollable element instead of the whole screen. " + MobileSelectorParamDescription },
                new ParamSpec { Name = "direction", Type = ParamType.String, Required = false, DefaultValue = "Down", Description = "Up, Down, Left, or Right. Default Down." },
                new ParamSpec { Name = "maxAttempts", Type = ParamType.Number, Required = false, DefaultValue = 10, Description = "Maximum number of scroll gestures to try before failing. Default 10." },
            }
        },
        new ActionSchema
        {
            ActionName = "pinch",
            Description = "A two-finger pinch on the first element matching a selector.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = MobileSelectorParamDescription },
                new ParamSpec { Name = "scale", Type = ParamType.Number, Required = true, Description = "Strictly positive. > 1 spreads fingers apart (zoom in); < 1 brings them together (zoom out)." },
            }
        },
        new ActionSchema
        {
            ActionName = "dragAndDrop",
            Description = "Drags the element matching 'from' to the element matching 'to'.",
            Parameters = new[]
            {
                new ParamSpec { Name = "from", Type = ParamType.String, Required = true, Description = MobileSelectorParamDescription },
                new ParamSpec { Name = "to", Type = ParamType.String, Required = true, Description = MobileSelectorParamDescription },
            }
        },
        new ActionSchema
        {
            ActionName = "assertVisible",
            Description = "Asserts an element matching a selector is displayed. A missing or hidden element both read as not-visible.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = MobileSelectorParamDescription },
            }
        },
    };

    /// <summary>MOB-T4: "XPath carries an explicit warning in the UI: on mobile it is genuinely
    /// slow, because the driver serializes the whole UI tree for each query. This is not a style
    /// preference; it's a measurable performance cliff." Zero schema/UI changes needed to say
    /// this -- same as every WEB-T4 selector param's own "also accepts an inline locator object"
    /// text, the generic schema-driven form already renders whatever Description says.</summary>
    private const string MobileSelectorParamDescription =
        "AccessibilityId or resource-id string (plain), XPath (starting with /), or an inline locator object " +
        "({primary, fallbacks, description, within, nth} -- AccessibilityId, ResourceId, IosPredicate, " +
        "IosClassChain, ClassName, Text, XPath) or an already-resolved Object Repository reference. " +
        "XPath works but is measurably slower on mobile than every other strategy here -- the driver " +
        "serializes the whole UI tree for each query -- prefer AccessibilityId when the app exposes one.";

    public async Task Setup(ExecutionContext ctx, CancellationToken ct)
    {
        if (ctx.Variables.GetValueOrDefault("mobile") is not IReadOnlyDictionary<string, object?> config)
            return;

        var profile = await BuildProfile(config, ctx.TenantId ?? TenantId.New(), ctx, ct);

        var options = new AppiumOptions { PlatformName = profile.Platform.ToString() };
        if (profile.DeviceName is not null) options.DeviceName = profile.DeviceName;
        options.AutomationName = profile.Platform == MobilePlatform.Ios ? "XCUITest" : "UiAutomator2";
        if (profile.App is not null) options.App = profile.App;

        // MOB-T3: the profile's own Reset strategy maps onto Appium's own noReset/fullReset
        // capabilities -- Full reinstalls (fullReset), None reuses state deliberately (noReset),
        // Fast (the default) takes neither flag, which is Appium's own ordinary per-session reset
        // behaviour. See ResetCapabilitiesFor's own doc comment for why this mapping, not a
        // hand-rolled uninstall/clear-data sequence.
        var (noReset, fullReset) = ResetCapabilitiesFor(profile.Reset);
        options.AddAdditionalAppiumOption("noReset", noReset);
        options.AddAdditionalAppiumOption("fullReset", fullReset);

        foreach (var (key, value) in profile.ExtraCapabilities)
        {
            if (value is not null)
                options.AddAdditionalAppiumOption(key, value);
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

        _sessions[ctx.RunId] = new MobileSession(driver, profile.Platform, _clock.GetUtcNow(), profile.SessionTimeout);
    }

    /// <summary>
    /// MOB-T3: "Three reset strategies, set per profile: Full (reinstall the app -- slowest,
    /// cleanest), Fast (clear app data between flows -- the default), None (reuse state
    /// deliberately, for flows that chain)." Appium's own W3C capabilities already express
    /// exactly this spectrum for both Android (UiAutomator2) and iOS (XCUITest) drivers:
    /// `fullReset: true` uninstalls and reinstalls the app (Full); `noReset: true` skips any
    /// reset entirely (None); neither flag set is each driver's own ordinary per-session reset
    /// (Fast) -- never a hand-rolled uninstall/clear-data sequence this module would have to
    /// reimplement and keep correct per platform. Public and pure for direct unit testing
    /// without a live Appium server, same reasoning as <see cref="ClassifySessionFailure"/>.
    /// </summary>
    public static (bool NoReset, bool FullReset) ResetCapabilitiesFor(ResetStrategy reset) => reset switch
    {
        ResetStrategy.Full => (NoReset: false, FullReset: true),
        ResetStrategy.None => (NoReset: true, FullReset: false),
        _ => (NoReset: false, FullReset: false), // Fast
    };

    /// <summary>
    /// Builds a typed <see cref="DeviceProfile"/> from the flow's own "mobile" config bag.
    /// "automationName" is no longer a required config key -- UiAutomator2/XCUITest are the
    /// only automation engines Appium's own tiers support per platform (the spec's own "one
    /// sentence" description), so there's nothing a flow author would ever set it to besides
    /// the platform default; keeping it configurable here would just be a way to misconfigure.
    /// farmCredentialRef, if present, is resolved now (not left as a literal) -- a step/profile
    /// author names which credential to use, never the credential itself.
    /// MOB-T3: public (not just <see cref="ClassifySessionFailure"/>) for the same reason --
    /// Setup() always fails without a live Appium server in this environment, so "reset"/
    /// "sessionTimeoutSeconds" parsing from the config bag needs to be directly testable without
    /// one too.
    /// </summary>
    public static async Task<DeviceProfile> BuildProfile(IReadOnlyDictionary<string, object?> config, TenantId tenantId, ExecutionContext ctx, CancellationToken ct)
    {
        var platformName = config.GetValueOrDefault("platformName") as string
            ?? throw new ArgumentException("mobile config requires 'platformName'.");
        var platform = platformName.Equals("iOS", StringComparison.OrdinalIgnoreCase) ? MobilePlatform.Ios : MobilePlatform.Android;
        var deviceName = config.GetValueOrDefault("deviceName") as string
            ?? throw new ArgumentException("mobile config requires 'deviceName'.");
        var app = config.GetValueOrDefault("app") as string;
        var appiumUrl = config.GetValueOrDefault("appiumUrl") as string ?? "http://localhost:4723";
        var farmCredentialRef = config.GetValueOrDefault("farmCredentialRef") as string;

        // MOB-T3: "reset"/"sessionTimeoutSeconds" are how FlowEngine.ResolveDeviceProfile (MOB-T2)
        // carries a resolved, persisted profile's own Reset/SessionTimeout through this same
        // literal-config bag -- without these two keys, a resolved profile's reset strategy would
        // silently never reach Setup(), always falling back to Fast/5-minutes regardless of what
        // the profile actually says. A flow author can also set either literally, same as every
        // other key here.
        var reset = config.GetValueOrDefault("reset") is string resetRaw
            && Enum.TryParse<ResetStrategy>(resetRaw, ignoreCase: true, out var parsedReset)
            ? parsedReset : ResetStrategy.Fast;
        var sessionTimeout = config.GetValueOrDefault("sessionTimeoutSeconds") is { } rawTimeout
            ? TimeSpan.FromSeconds(Convert.ToDouble(rawTimeout)) : TimeSpan.FromMinutes(5);

        var reserved = new HashSet<string>
        {
            "platformName", "deviceName", "app", "appiumUrl", "farmCredentialRef", "automationName",
            "reset", "sessionTimeoutSeconds",
        };
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

        // MOB-T2: this ad hoc path (literal config in ctx.Variables["mobile"]) builds a
        // transient, never-persisted DeviceProfile -- deliberately not looked up by name here.
        // FlowEngine.ResolveDeviceProfile (MOB-T2) is what turns a flow's "profileName" into
        // this same literal-config shape *before* Setup() ever sees it, so this method stays
        // unaware of persistence either way. EndpointKind is metadata-only at this layer (not
        // yet used to vary connection/retry behaviour -- a later task's job), so a farm
        // credential's presence is the only signal available to infer it from literal config.
        return DeviceProfile.Create(
            deviceName, tenantId, platform, platformVersion: null, deviceName, app, appiumUrl,
            farmCredentialRef is not null ? DeviceEndpointKind.Farm : DeviceEndpointKind.Local,
            farmCredentialRef, reset, sessionTimeout, extraCapabilities);
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
        if (_sessions.TryRemove(ctx.RunId, out var session))
            session.Driver.Quit();
        return Task.CompletedTask;
    }

    public async Task<StepOutcome> Run(string action, IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(ctx.RunId, out var session))
            return StepOutcome.Failed("MobileModule not set up — call setup() first.");

        // MOB-T3: "a configurable session timeout, after which the session is force-released
        // and the run fails with a clear message." Checked lazily here, on the next action
        // against an already-expired session, rather than a background timer -- a session a
        // flow never touches again costs nothing to expire, and FlowEngine's own Teardown call
        // still runs normally afterward (TryRemove here already took it out, so Teardown's own
        // TryRemove is a safe no-op, same shape as every other "nothing to tear down" case).
        if (session.IsExpired(_clock.GetUtcNow()))
        {
            _sessions.TryRemove(ctx.RunId, out _);
            try { session.Driver.Quit(); } catch { /* best-effort release of an already-stale session */ }
            return StepOutcome.Failed(
                $"Mobile session exceeded its {session.Timeout} timeout and was force-released. " +
                "Increase the device profile's session timeout, or reduce how long the flow runs before this step.");
        }

        var driver = session.Driver;
        try
        {
            switch (action)
            {
                case "tap":
                    {
                        var raw = RequireSelectorParam(parameters);
                        var (target, info, name) = await ResolveLocatorTarget(driver, raw, session.Platform, ct);
                        AppiumGestureExecutor.Tap(driver, target);
                        return StepOutcome.Passed($"Tapped {name}", locatorResolution: info);
                    }
                case "type":
                    {
                        var raw = RequireSelectorParam(parameters);
                        var value = RequireString(parameters, "value");
                        var (target, info, name) = await ResolveLocatorTarget(driver, raw, session.Platform, ct);
                        target.SendKeys(value);
                        return StepOutcome.Passed($"Typed into {name}", locatorResolution: info);
                    }
                case "doubleTap":
                    {
                        var raw = RequireSelectorParam(parameters);
                        var (target, info, name) = await ResolveLocatorTarget(driver, raw, session.Platform, ct);
                        AppiumGestureExecutor.DoubleTap(driver, target);
                        return StepOutcome.Passed($"Double-tapped {name}", locatorResolution: info);
                    }
                case "longPress":
                    {
                        var raw = RequireSelectorParam(parameters);
                        var durationMs = OptionalInt(parameters, "durationMs") ?? 1000;
                        var (target, info, name) = await ResolveLocatorTarget(driver, raw, session.Platform, ct);
                        AppiumGestureExecutor.LongPress(driver, target, TimeSpan.FromMilliseconds(durationMs));
                        return StepOutcome.Passed($"Long-pressed {name}", locatorResolution: info);
                    }
                case "swipe":
                    {
                        // Legacy absolute-pixel path, unchanged since MOB-T1 -- zero behavioural
                        // change for an existing flow that already sets all four.
                        if (parameters.ContainsKey("fromX") || parameters.ContainsKey("fromY")
                            || parameters.ContainsKey("toX") || parameters.ContainsKey("toY"))
                        {
                            var fromX = RequireInt(parameters, "fromX");
                            var fromY = RequireInt(parameters, "fromY");
                            var toX = RequireInt(parameters, "toX");
                            var toY = RequireInt(parameters, "toY");
                            AppiumGestureExecutor.SwipeBetween(driver, fromX, fromY, toX, toY);
                            return StepOutcome.Passed($"Swiped ({fromX},{fromY}) -> ({toX},{toY})");
                        }

                        var direction = RequireDirection(parameters);
                        var distance = OptionalDouble(parameters, "distance") ?? 0.5;
                        var (originX, originY, width, height, scopeInfo, scopeName) =
                            await ResolveSwipeRegion(driver, parameters, "within", session.Platform, ct);
                        var (fromRel, toRel) = AppiumGestureExecutor.DirectionToPoints(direction, distance);
                        var (absFromX, absFromY) = AppiumGestureExecutor.ToAbsolute(fromRel, originX, originY, width, height);
                        var (absToX, absToY) = AppiumGestureExecutor.ToAbsolute(toRel, originX, originY, width, height);
                        AppiumGestureExecutor.SwipeBetween(driver, absFromX, absFromY, absToX, absToY);
                        return StepOutcome.Passed($"Swiped {direction}{scopeName}", locatorResolution: scopeInfo);
                    }
                case "swipeBetween":
                    {
                        var fromX = RequireDouble(parameters, "fromX");
                        var fromY = RequireDouble(parameters, "fromY");
                        var toX = RequireDouble(parameters, "toX");
                        var toY = RequireDouble(parameters, "toY");
                        var durationMs = OptionalInt(parameters, "durationMs") ?? 300;
                        var (originX, originY, width, height, scopeInfo, scopeName) =
                            await ResolveSwipeRegion(driver, parameters, "within", session.Platform, ct);
                        var (absFromX, absFromY) = AppiumGestureExecutor.ToAbsolute(new RelativePoint(fromX, fromY), originX, originY, width, height);
                        var (absToX, absToY) = AppiumGestureExecutor.ToAbsolute(new RelativePoint(toX, toY), originX, originY, width, height);
                        AppiumGestureExecutor.SwipeBetween(driver, absFromX, absFromY, absToX, absToY, TimeSpan.FromMilliseconds(durationMs));
                        return StepOutcome.Passed($"Swiped ({fromX},{fromY}) -> ({toX},{toY}){scopeName}", locatorResolution: scopeInfo);
                    }
                case "scrollTo":
                    {
                        var raw = RequireSelectorParam(parameters);
                        var direction = OptionalDirection(parameters) ?? SwipeDirection.Down;
                        var maxAttempts = OptionalInt(parameters, "maxAttempts") ?? 10;
                        var (originX, originY, width, height, _, _) =
                            await ResolveSwipeRegion(driver, parameters, "container", session.Platform, ct);

                        (IWebElement Target, LocatorResolutionInfo? Info, string Name)? found = null;
                        async Task<bool> TryResolve()
                        {
                            try
                            {
                                found = await ResolveLocatorTarget(driver, raw, session.Platform, ct);
                                return found.Value.Target.Displayed;
                            }
                            catch (MobileSelectorNotFoundException)
                            {
                                found = null;
                                return false;
                            }
                        }
                        Task ScrollOnce()
                        {
                            var (fromRel, toRel) = AppiumGestureExecutor.DirectionToPoints(direction, 0.5);
                            var (fx, fy) = AppiumGestureExecutor.ToAbsolute(fromRel, originX, originY, width, height);
                            var (tx, ty) = AppiumGestureExecutor.ToAbsolute(toRel, originX, originY, width, height);
                            AppiumGestureExecutor.SwipeBetween(driver, fx, fy, tx, ty);
                            return Task.CompletedTask;
                        }

                        var visible = await AppiumGestureExecutor.TryUntilAsync(TryResolve, ScrollOnce, maxAttempts, ct);
                        if (!visible || found is null)
                            throw new MobileSelectorNotFoundException(
                                $"No element matched after scrolling {direction} up to {maxAttempts} time(s).");
                        return StepOutcome.Passed($"Scrolled to {found.Value.Name}", locatorResolution: found.Value.Info);
                    }
                case "pinch":
                    {
                        var raw = RequireSelectorParam(parameters);
                        var scale = RequireDouble(parameters, "scale");
                        AppiumGestureExecutor.ValidateScale(scale);
                        var (target, info, name) = await ResolveLocatorTarget(driver, raw, session.Platform, ct);
                        var (centerX, centerY) = AppiumGestureExecutor.ElementCenter(target);
                        var startOffset = Math.Max(1, Math.Min(target.Size.Width, target.Size.Height) / 4);
                        var endOffset = (int)Math.Round(startOffset * scale);
                        AppiumGestureExecutor.Pinch(driver, centerX, centerY, startOffset, endOffset);
                        return StepOutcome.Passed($"Pinched {name} by {scale}x", locatorResolution: info);
                    }
                case "dragAndDrop":
                    {
                        var fromRaw = RequireLocatorParam(parameters, "from");
                        var toRaw = RequireLocatorParam(parameters, "to");
                        var (fromTarget, fromInfo, fromName) = await ResolveLocatorTarget(driver, fromRaw, session.Platform, ct);
                        var (toTarget, _, toName) = await ResolveLocatorTarget(driver, toRaw, session.Platform, ct);
                        var (fx, fy) = AppiumGestureExecutor.ElementCenter(fromTarget);
                        var (tx, ty) = AppiumGestureExecutor.ElementCenter(toTarget);
                        AppiumGestureExecutor.DragAndDrop(driver, fx, fy, tx, ty);
                        return StepOutcome.Passed($"Dragged {fromName} to {toName}", locatorResolution: fromInfo);
                    }
                case "assertVisible":
                    {
                        var raw = RequireSelectorParam(parameters);
                        bool visible;
                        string name;
                        try
                        {
                            var (target, _, resolvedName) = await ResolveLocatorTarget(driver, raw, session.Platform, ct);
                            visible = target.Displayed;
                            name = resolvedName;
                        }
                        catch
                        {
                            visible = false;
                            name = DescribeSelectorParam(raw);
                        }
                        return visible
                            ? StepOutcome.Passed($"{name} is visible.")
                            : StepOutcome.Failed($"{name} is not visible.");
                    }
                default:
                    return StepOutcome.Failed($"Unknown mobile action \"{action}\".");
            }
        }
        catch (Exception ex)
        {
            var data = new Dictionary<string, object?>();
            var log = await TryCaptureAppiumLog(driver, ct);
            if (log is not null)
                data["appiumServerLog"] = log;

            // MOB-T4's own checklist: "failure diagnostics include a screenshot and the UI
            // hierarchy XML" -- scoped to a locator-resolution failure specifically (the mobile
            // equivalent of WEB-T4's screenshot-on-selector-failure), not every possible action
            // failure (a swipe failure, say, has nothing to do with a selector at all).
            if (ex is MobileSelectorNotFoundException)
            {
                var diagnostics = await TryCaptureLocatorFailureDiagnostics(driver, ct);
                if (diagnostics is not null)
                    foreach (var (key, value) in diagnostics)
                        data[key] = value;
            }

            return StepOutcome.Failed(ex.Message, data.Count > 0 ? data : null);
        }
    }

    /// <summary>MOB-T4: "failure diagnostics include a screenshot and the UI hierarchy XML."
    /// Best-effort, same tolerance as <see cref="TryCaptureAppiumLog"/> -- a driver/platform that
    /// doesn't support one must never turn a real step failure into a different, more confusing
    /// failure about diagnostics.</summary>
    private static async Task<IReadOnlyDictionary<string, object?>?> TryCaptureLocatorFailureDiagnostics(AppiumDriver driver, CancellationToken ct)
    {
        var diagnostics = new Dictionary<string, object?>();
        try
        {
            var screenshot = await Task.Run(() => driver.GetScreenshot(), ct);
            diagnostics["screenshot"] = Convert.ToBase64String(screenshot.AsByteArray);
        }
        catch { /* best-effort */ }

        try
        {
            diagnostics["uiHierarchy"] = await Task.Run(() => driver.PageSource, ct);
        }
        catch { /* best-effort */ }

        return diagnostics.Count > 0 ? diagnostics : null;
    }

    /// <summary>MOB-T4: a step's "selector" param is either the legacy plain string (zero
    /// behavioural change), an already-resolved Yukti.Domain.UiVocabulary.Locator
    /// (FlowEngine.ResolveLocatorRef already did this for a RepositoryRef, WEB-T2's mechanism,
    /// engine-neutral, reused as-is), or an inline locator object a flow author wrote directly
    /// in the step's params (LocatorParamMapper, shared with WebModule, never duplicated).</summary>
    private static async Task<(IWebElement Target, LocatorResolutionInfo? Info, string Name)> ResolveLocatorTarget(
        AppiumDriver driver, object raw, MobilePlatform platform, CancellationToken ct)
    {
        if (raw is string selector)
            return (ResolveElement(driver, selector), null, selector);

        var locator = raw as Locator ?? LocatorParamMapper.Parse(raw);
        var outcome = await AppiumLocatorResolver.ResolveAsync(driver, locator, platform, DefaultLocatorTimeout, ct);
        var name = locator.Description ?? "locator";
        if (!outcome.Succeeded)
            throw new MobileSelectorNotFoundException(outcome.FailureDetail!);
        return (outcome.Resolved!, outcome.Info, name);
    }

    private static object RequireSelectorParam(IReadOnlyDictionary<string, object?> parameters) =>
        parameters.GetValueOrDefault("selector")
            ?? throw new ArgumentException("mobile.selector is required.");

    private static string DescribeSelectorParam(object raw) => raw switch
    {
        string s => s,
        Locator l => l.Description ?? "locator",
        IReadOnlyDictionary<string, object?> d when d.GetValueOrDefault("description") is string desc => desc,
        _ => "locator",
    };

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

    private static double RequireDouble(IReadOnlyDictionary<string, object?> parameters, string name) =>
        parameters.TryGetValue(name, out var value) && value is not null
            ? Convert.ToDouble(value)
            : throw new ArgumentException($"mobile action requires a '{name}' parameter.");

    private static double? OptionalDouble(IReadOnlyDictionary<string, object?> parameters, string name) =>
        parameters.GetValueOrDefault(name) is { } value ? Convert.ToDouble(value) : null;

    private static int? OptionalInt(IReadOnlyDictionary<string, object?> parameters, string name) =>
        parameters.GetValueOrDefault(name) is { } value ? Convert.ToInt32(value) : null;

    /// <summary>MOB-T5: a gesture param shaped like a selector (<c>from</c>, <c>to</c>) but not
    /// named "selector" -- same resolution rules (plain string / Object Repository reference /
    /// inline locator object), just a different param name.</summary>
    private static object RequireLocatorParam(IReadOnlyDictionary<string, object?> parameters, string name) =>
        parameters.GetValueOrDefault(name)
            ?? throw new ArgumentException($"mobile.{name} is required.");

    private static SwipeDirection RequireDirection(IReadOnlyDictionary<string, object?> parameters) =>
        OptionalDirection(parameters)
            ?? throw new ArgumentException("mobile.swipe requires either a 'direction', or all four of fromX/fromY/toX/toY.");

    private static SwipeDirection? OptionalDirection(IReadOnlyDictionary<string, object?> parameters) =>
        parameters.GetValueOrDefault("direction") is string raw
            ? Enum.TryParse<SwipeDirection>(raw, ignoreCase: true, out var direction)
                ? direction
                : throw new ArgumentException($"'{raw}' is not a recognized swipe direction. Supported: Up, Down, Left, Right.")
            : null;

    /// <summary>MOB-T5: the region a relative swipe/scroll is measured against -- an element's
    /// own bounds when <paramref name="paramName"/> (<c>within</c> or <c>container</c>) is set,
    /// otherwise the whole screen (<c>driver.Manage().Window.Size</c>, origin (0,0)).</summary>
    private static async Task<(int OriginX, int OriginY, int Width, int Height, LocatorResolutionInfo? Info, string ScopeSuffix)> ResolveSwipeRegion(
        AppiumDriver driver, IReadOnlyDictionary<string, object?> parameters, string paramName, MobilePlatform platform, CancellationToken ct)
    {
        if (parameters.GetValueOrDefault(paramName) is { } raw)
        {
            var (target, info, name) = await ResolveLocatorTarget(driver, raw, platform, ct);
            return (target.Location.X, target.Location.Y, target.Size.Width, target.Size.Height, info, $" within {name}");
        }

        var screen = driver.Manage().Window.Size;
        return (0, 0, screen.Width, screen.Height, null, "");
    }
}
