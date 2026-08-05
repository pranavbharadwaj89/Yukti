using System.Collections.Concurrent;
using Microsoft.Playwright;
using Yukti.Contracts;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// Real, working browser automation via Playwright (Chromium). Direct port
/// of the original TS prototype's WebModule to the formal IAutomationModule
/// contract — same six actions, same params, same "never throws out of
/// Run" error shape. (Volume 1 Part III §18)
///
/// Unlike ApiModule/LogsModule, this module is genuinely stateful: one
/// Browser/Page must persist across every step of a single flow run (so
/// `navigate` in step 1 and `click` in step 2 share the same page), then
/// close when the run ends. Because WebModule is registered as a
/// singleton, that state cannot live in an instance field the way the TS
/// prototype's single `page` field does — two concurrent flow runs using
/// `web` would stomp each other's page. Instead each run's session is
/// keyed by ExecutionContext.RunId, created in Setup and removed in
/// Teardown — both of which FlowEngine now calls for every module a flow's
/// steps actually reference (see FlowEngine.Execute).
/// </summary>
public sealed class WebModule : IAutomationModule
{
    private sealed class WebSession
    {
        public required IPlaywright Playwright { get; init; }
        public required IBrowser Browser { get; init; }
        public required IBrowserContext Context { get; init; }
        public required IPage Page { get; init; }
    }

    private readonly ConcurrentDictionary<FlowRunId, WebSession> _sessions = new();

    public ModuleKind Kind => ModuleKind.Web;
    public string ContractVersion => "1.0.0";

    public IReadOnlyList<ActionSchema> GetSupportedActions() => new[]
    {
        new ActionSchema
        {
            ActionName = "navigate",
            Description = "Navigates the shared page to a URL.",
            Parameters = new[]
            {
                new ParamSpec { Name = "url", Type = ParamType.String, Required = true },
            }
        },
        new ActionSchema
        {
            ActionName = "click",
            Description = "Clicks the first element matching a selector.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true },
                new ParamSpec { Name = "timeoutMs", Type = ParamType.Number, Required = false, DefaultValue = 5000 },
            }
        },
        new ActionSchema
        {
            ActionName = "fill",
            Description = "Fills the first element matching a selector with a value.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true },
                new ParamSpec { Name = "value", Type = ParamType.String, Required = true },
            }
        },
        new ActionSchema
        {
            ActionName = "assertText",
            Description = "Asserts a selector's text content, exactly or via substring containment.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true },
                new ParamSpec { Name = "expected", Type = ParamType.String, Required = true },
                new ParamSpec { Name = "mode", Type = ParamType.String, Required = false, Description = "\"contains\" for substring match; omit for exact match." },
            }
        },
        new ActionSchema
        {
            ActionName = "screenshot",
            Description = "Captures a screenshot of the current page to local disk.",
            Parameters = new[]
            {
                new ParamSpec { Name = "path", Type = ParamType.String, Required = false, Description = "Defaults to screenshot-<timestamp>.png." },
                new ParamSpec { Name = "fullPage", Type = ParamType.Boolean, Required = false, DefaultValue = false },
            }
        },
        new ActionSchema
        {
            ActionName = "waitForSelector",
            Description = "Waits for a selector to appear.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true },
                new ParamSpec { Name = "timeoutMs", Type = ParamType.Number, Required = false, DefaultValue = 10000 },
            }
        },
    };

    public async Task Setup(ExecutionContext ctx, CancellationToken ct)
    {
        var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        _sessions[ctx.RunId] = new WebSession { Playwright = playwright, Browser = browser, Context = context, Page = page };
    }

    public async Task Teardown(ExecutionContext ctx, CancellationToken ct)
    {
        if (!_sessions.TryRemove(ctx.RunId, out var session))
            return;

        await session.Context.CloseAsync();
        await session.Browser.CloseAsync();
        session.Playwright.Dispose();
    }

    public async Task<StepOutcome> Run(string action, IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(ctx.RunId, out var session))
            return StepOutcome.Failed("WebModule not set up — call setup() first.");

        var page = session.Page;

        try
        {
            switch (action)
            {
                case "navigate":
                {
                    var url = parameters.GetValueOrDefault("url") as string
                        ?? throw new ArgumentException("web.navigate requires a 'url' parameter.");
                    await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                    return StepOutcome.Passed($"Navigated to {url}");
                }
                case "click":
                {
                    var selector = parameters.GetValueOrDefault("selector") as string
                        ?? throw new ArgumentException("web.click requires a 'selector' parameter.");
                    var timeoutMs = ToFloat(parameters.GetValueOrDefault("timeoutMs")) ?? 5000f;
                    await page.Locator(selector).ClickAsync(new LocatorClickOptions { Timeout = timeoutMs });
                    return StepOutcome.Passed($"Clicked {selector}");
                }
                case "fill":
                {
                    var selector = parameters.GetValueOrDefault("selector") as string
                        ?? throw new ArgumentException("web.fill requires a 'selector' parameter.");
                    var value = parameters.GetValueOrDefault("value") as string
                        ?? throw new ArgumentException("web.fill requires a 'value' parameter.");
                    await page.Locator(selector).FillAsync(value);
                    return StepOutcome.Passed($"Filled {selector}");
                }
                case "assertText":
                {
                    var selector = parameters.GetValueOrDefault("selector") as string
                        ?? throw new ArgumentException("web.assertText requires a 'selector' parameter.");
                    var expected = parameters.GetValueOrDefault("expected") as string
                        ?? throw new ArgumentException("web.assertText requires an 'expected' parameter.");
                    var mode = parameters.GetValueOrDefault("mode") as string;

                    var actual = (await page.Locator(selector).TextContentAsync())?.Trim() ?? "";
                    var matched = mode == "contains" ? actual.Contains(expected) : actual == expected;
                    return matched
                        ? StepOutcome.Passed($"Text of {selector} matched.")
                        : StepOutcome.Failed($"Expected {selector} text {(mode == "contains" ? "to contain" : "to equal")} \"{expected}\", got \"{actual}\".");
                }
                case "screenshot":
                {
                    var path = parameters.GetValueOrDefault("path") as string ?? $"screenshot-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.png";
                    var fullPage = parameters.GetValueOrDefault("fullPage") as bool? ?? false;
                    await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = fullPage });
                    return StepOutcome.Passed($"Screenshot saved to {path}", new { path });
                }
                case "waitForSelector":
                {
                    var selector = parameters.GetValueOrDefault("selector") as string
                        ?? throw new ArgumentException("web.waitForSelector requires a 'selector' parameter.");
                    var timeoutMs = ToFloat(parameters.GetValueOrDefault("timeoutMs")) ?? 10000f;
                    await page.WaitForSelectorAsync(selector, new PageWaitForSelectorOptions { Timeout = timeoutMs });
                    return StepOutcome.Passed($"{selector} appeared.");
                }
                default:
                    return StepOutcome.Failed($"Unknown web action \"{action}\".");
            }
        }
        catch (Exception ex)
        {
            return StepOutcome.Failed(ex.Message);
        }
    }

    private static float? ToFloat(object? value) => value is null ? null : Convert.ToSingle(value);
}
