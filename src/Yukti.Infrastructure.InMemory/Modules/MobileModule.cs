using System.Collections.Concurrent;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using OpenQA.Selenium.Appium.iOS;
using OpenQA.Selenium.Interactions;
using Yukti.Contracts;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// Real mobile UI automation via Appium (Android/iOS). Direct port of the
/// original TS prototype's MobileModule to the formal IAutomationModule
/// contract — same four actions, same "never throws out of Run" shape.
/// (Volume 1 Part III §18)
///
/// One real deviation from the TS version, not a silent one: the TS
/// MobileModule takes fixed MobileCapabilities in its own constructor — one
/// module instance equals one fixed device for the module's whole
/// lifetime. That doesn't fit here: MobileModule is a DI singleton shared
/// by every tenant/flow via ModuleRegistry, so capabilities can't be
/// baked in at construction. Instead Setup(ctx) reads a "mobile" object out
/// of ctx.Variables (the same bag TriggerFlowRunCommand's
/// variableOverrides already populates) — platformName, deviceName, app,
/// automationName, optional appiumUrl. Missing config means Setup just
/// doesn't create a session (not an exception — a flow mixing module kinds
/// shouldn't abort over unrelated missing config); Run then fails with the
/// same "not set up" message the TS version uses, at the point that
/// actually matters.
///
/// Requires a running Appium server + attached device/emulator/simulator
/// reachable at appiumUrl — this module only connects to one, exactly like
/// the TS version; it does not start Appium itself.
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
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true },
                new ParamSpec { Name = "value", Type = ParamType.String, Required = true },
            }
        },
        new ActionSchema
        {
            ActionName = "swipe",
            Description = "Synthesizes a touch swipe gesture from one point to another.",
            Parameters = new[]
            {
                new ParamSpec { Name = "fromX", Type = ParamType.Number, Required = true },
                new ParamSpec { Name = "fromY", Type = ParamType.Number, Required = true },
                new ParamSpec { Name = "toX", Type = ParamType.Number, Required = true },
                new ParamSpec { Name = "toY", Type = ParamType.Number, Required = true },
            }
        },
        new ActionSchema
        {
            ActionName = "assertVisible",
            Description = "Asserts an element matching a selector is displayed. A missing or hidden element both read as not-visible.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true },
            }
        },
    };

    public Task Setup(ExecutionContext ctx, CancellationToken ct)
    {
        if (ctx.Variables.GetValueOrDefault("mobile") is not IReadOnlyDictionary<string, object?> config)
            return Task.CompletedTask;

        var platformName = config.GetValueOrDefault("platformName") as string
            ?? throw new ArgumentException("mobile config requires 'platformName'.");
        var deviceName = config.GetValueOrDefault("deviceName") as string
            ?? throw new ArgumentException("mobile config requires 'deviceName'.");
        var app = config.GetValueOrDefault("app") as string;
        var automationName = config.GetValueOrDefault("automationName") as string
            ?? throw new ArgumentException("mobile config requires 'automationName'.");
        var appiumUrl = config.GetValueOrDefault("appiumUrl") as string ?? "http://localhost:4723";

        var options = new AppiumOptions();
        options.PlatformName = platformName;
        options.AddAdditionalAppiumOption("appium:deviceName", deviceName);
        options.AddAdditionalAppiumOption("appium:automationName", automationName);
        if (app is not null)
            options.AddAdditionalAppiumOption("appium:app", app);
        foreach (var (key, value) in config)
        {
            if (key is "platformName" or "deviceName" or "app" or "automationName" or "appiumUrl")
                continue;
            if (value is not null)
                options.AddAdditionalAppiumOption(key, value);
        }

        var remoteAddress = new Uri(appiumUrl);
        AppiumDriver driver = platformName.Equals("iOS", StringComparison.OrdinalIgnoreCase)
            ? new IOSDriver(remoteAddress, options)
            : new AndroidDriver(remoteAddress, options);

        _sessions[ctx.RunId] = driver;
        return Task.CompletedTask;
    }

    public Task Teardown(ExecutionContext ctx, CancellationToken ct)
    {
        if (_sessions.TryRemove(ctx.RunId, out var driver))
            driver.Quit();
        return Task.CompletedTask;
    }

    public Task<StepOutcome> Run(string action, IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(ctx.RunId, out var driver))
            return Task.FromResult(StepOutcome.Failed("MobileModule not set up — call setup() first."));

        try
        {
            switch (action)
            {
                case "tap":
                {
                    var selector = RequireString(parameters, "selector");
                    ResolveElement(driver, selector).Click();
                    return Task.FromResult(StepOutcome.Passed($"Tapped {selector}"));
                }
                case "type":
                {
                    var selector = RequireString(parameters, "selector");
                    var value = RequireString(parameters, "value");
                    ResolveElement(driver, selector).SendKeys(value);
                    return Task.FromResult(StepOutcome.Passed($"Typed into {selector}"));
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
                    return Task.FromResult(StepOutcome.Passed($"Swiped ({fromX},{fromY}) -> ({toX},{toY})"));
                }
                case "assertVisible":
                {
                    var selector = RequireString(parameters, "selector");
                    bool visible;
                    try { visible = ResolveElement(driver, selector).Displayed; }
                    catch { visible = false; }
                    return Task.FromResult(visible
                        ? StepOutcome.Passed($"{selector} is visible.")
                        : StepOutcome.Failed($"{selector} is not visible."));
                }
                default:
                    return Task.FromResult(StepOutcome.Failed($"Unknown mobile action \"{action}\"."));
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(StepOutcome.Failed(ex.Message));
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
