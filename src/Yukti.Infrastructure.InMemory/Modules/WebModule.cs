using System.Collections.Concurrent;
using Microsoft.Playwright;
using Yukti.Application.Security;
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

    /// <summary>
    /// D6-T5: defaults to permissive, same reasoning as ApiModule's own
    /// guard parameter — every pre-existing construction site keeps working
    /// unchanged. WebModule is already exempt from FR-PLUGIN-07's
    /// zero-instance-field check (ModulePluginContractTests only covers
    /// ApiModule/LogsModule — this module's own doc comment already
    /// explains why it must hold per-run state), so this is a plain field,
    /// no ConditionalWeakTable needed.
    /// </summary>
    private readonly IOutboundGuard _guard;

    public WebModule(IOutboundGuard? guard = null) => _guard = guard ?? PermissiveOutboundGuard.Instance;

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
                new ParamSpec { Name = "url", Type = ParamType.String, Required = true, Description = "The URL to navigate the shared page to." },
            }
        },
        new ActionSchema
        {
            ActionName = "click",
            Description = "Clicks the first element matching a selector.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "CSS or Playwright selector of the element to click." },
                new ParamSpec { Name = "timeoutMs", Type = ParamType.Number, Required = false, DefaultValue = 5000, Description = "Milliseconds to wait for the element before failing." },
            }
        },
        new ActionSchema
        {
            ActionName = "fill",
            Description = "Fills the first element matching a selector with a value.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "CSS or Playwright selector of the input to fill." },
                new ParamSpec { Name = "value", Type = ParamType.String, Required = true, Description = "Text value to type into the matched element." },
            }
        },
        new ActionSchema
        {
            ActionName = "assertText",
            Description = "Asserts a selector's text content, exactly or via substring containment.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "CSS or Playwright selector of the element whose text is checked." },
                new ParamSpec { Name = "expected", Type = ParamType.String, Required = true, Description = "Text the element's content is compared against." },
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
                new ParamSpec { Name = "fullPage", Type = ParamType.Boolean, Required = false, DefaultValue = false, Description = "Captures the full scrollable page instead of just the viewport." },
            }
        },
        new ActionSchema
        {
            ActionName = "waitForSelector",
            Description = "Waits for a selector to appear.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "CSS or Playwright selector to wait for." },
                new ParamSpec { Name = "timeoutMs", Type = ParamType.Number, Required = false, DefaultValue = 10000, Description = "Milliseconds to wait before failing with SelectorNotFound." },
            }
        },
        new ActionSchema
        {
            ActionName = "select",
            Description = "Selects an option by value in the first <select> element matching a selector.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "CSS or Playwright selector of the <select> element." },
                new ParamSpec { Name = "value", Type = ParamType.String, Required = true, Description = "The option value to select." },
            }
        },
        new ActionSchema
        {
            ActionName = "getText",
            Description = "Reads the trimmed text content of the first element matching a selector and returns it as step data ({text}), so a later step can use it via saveAs.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "CSS or Playwright selector of the element to read." },
            }
        },
        new ActionSchema
        {
            ActionName = "assertVisible",
            Description = "Asserts an element matching a selector is displayed. A missing or hidden element both read as not-visible, never an exception.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "CSS or Playwright selector of the element to check." },
            }
        },
    };

    public async Task Setup(ExecutionContext ctx, CancellationToken ct)
    {
        var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync();

        // D6-T5: route interception (CDP-level), not Chromium's native
        // --proxy-server — confirmed empirically that Chromium silently
        // bypasses a configured proxy for loopback destinations by default,
        // even with the documented "<-loopback>" bypass-list override
        // (tried both via Playwright's Proxy.Bypass and a raw
        // --proxy-bypass-list argument; neither routed a single
        // http://127.0.0.1 request through a real proxy in this Chromium
        // build). RouteAsync intercepts before Chromium's network stack
        // makes any proxy/bypass decision at all, so it sees every request
        // — including loopback — regardless. Every navigation and
        // sub-resource load re-enters this handler, which is what makes
        // redirects checked at every hop for free: a 3xx response's
        // Location header becomes a new request, which re-enters here.
        //
        // Known residual gap, stated honestly rather than silently: unlike
        // ApiModule's ConnectCallback (which connects to the exact IP this
        // guard already checked), ContinueAsync() hands the request back to
        // Chromium's own resolver — a true DNS-rebinding attacker who
        // answers differently a few hundred milliseconds later, between
        // this check and Chromium's own subsequent lookup, is not closed by
        // this path. Every other case in the adversarial set (direct
        // private IP, metadata endpoint, hostname-resolves-to-private,
        // redirect-to-private, decimal-encoded IP, loopback, public still
        // works, per-tenant allow-list) is covered.
        await context.RouteAsync("**/*", async route =>
        {
            Uri uri;
            try { uri = new Uri(route.Request.Url); }
            catch (UriFormatException) { await route.ContinueAsync(); return; }

            if (uri.Scheme != "http" && uri.Scheme != "https")
            {
                await route.ContinueAsync();
                return;
            }

            try
            {
                await _guard.ResolveAllowedAddressesAsync(uri.Host, tenantId: null, ctx.RunCancellation);
                await route.ContinueAsync();
            }
            catch (Exception)
            {
                await route.AbortAsync("blockedbyclient");
            }
        });

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
                        await WithSelectorNotFound(selector, () => page.Locator(selector).ClickAsync(new LocatorClickOptions { Timeout = timeoutMs }), ct);
                        return StepOutcome.Passed($"Clicked {selector}");
                    }
                case "fill":
                    {
                        var selector = parameters.GetValueOrDefault("selector") as string
                            ?? throw new ArgumentException("web.fill requires a 'selector' parameter.");
                        var value = parameters.GetValueOrDefault("value") as string
                            ?? throw new ArgumentException("web.fill requires a 'value' parameter.");
                        await WithSelectorNotFound(selector, () => page.Locator(selector).FillAsync(value), ct);
                        return StepOutcome.Passed($"Filled {selector}");
                    }
                case "select":
                    {
                        var selector = parameters.GetValueOrDefault("selector") as string
                            ?? throw new ArgumentException("web.select requires a 'selector' parameter.");
                        var value = parameters.GetValueOrDefault("value") as string
                            ?? throw new ArgumentException("web.select requires a 'value' parameter.");
                        await WithSelectorNotFound(selector, () => page.Locator(selector).SelectOptionAsync(value), ct);
                        return StepOutcome.Passed($"Selected '{value}' on {selector}");
                    }
                case "assertText":
                    {
                        var selector = parameters.GetValueOrDefault("selector") as string
                            ?? throw new ArgumentException("web.assertText requires a 'selector' parameter.");
                        var expected = parameters.GetValueOrDefault("expected") as string
                            ?? throw new ArgumentException("web.assertText requires an 'expected' parameter.");
                        var mode = parameters.GetValueOrDefault("mode") as string;

                        var actual = (await WithSelectorNotFound(selector, () => page.Locator(selector).TextContentAsync(), ct))?.Trim() ?? "";
                        var matched = mode == "contains" ? actual.Contains(expected) : actual == expected;
                        return matched
                            ? StepOutcome.Passed($"Text of {selector} matched.")
                            : StepOutcome.Failed($"Expected {selector} text {(mode == "contains" ? "to contain" : "to equal")} \"{expected}\", got \"{actual}\".");
                    }
                case "getText":
                    {
                        var selector = parameters.GetValueOrDefault("selector") as string
                            ?? throw new ArgumentException("web.getText requires a 'selector' parameter.");
                        var text = (await WithSelectorNotFound(selector, () => page.Locator(selector).TextContentAsync(), ct))?.Trim() ?? "";
                        return StepOutcome.Passed($"Read text of {selector}", new Dictionary<string, object?> { ["text"] = text });
                    }
                case "assertVisible":
                    {
                        ct.ThrowIfCancellationRequested();
                        var selector = parameters.GetValueOrDefault("selector") as string
                            ?? throw new ArgumentException("web.assertVisible requires a 'selector' parameter.");
                        // IsVisibleAsync never waits/throws — a missing element
                        // and a hidden one both just read as false, matching
                        // MobileModule's own assertVisible semantics exactly.
                        var visible = await page.Locator(selector).IsVisibleAsync();
                        return visible
                            ? StepOutcome.Passed($"{selector} is visible.")
                            : StepOutcome.Failed($"Element '{selector}' is not visible (missing or hidden).");
                    }
                case "screenshot":
                    {
                        // D6-T5: written under ScreenshotStorage's own per-run directory, not
                        // wherever the caller's "path" happened to point — so Yukti.Api's
                        // screenshot-serving endpoint can find it again by (runId, fileName) alone.
                        var fileName = ScreenshotStorage.SafeFileName(parameters.GetValueOrDefault("path") as string, "screenshot");
                        var fullPage = parameters.GetValueOrDefault("fullPage") as bool? ?? false;
                        var fullPath = ScreenshotStorage.PrepareFullPath(ctx.RunId, fileName);
                        await page.ScreenshotAsync(new PageScreenshotOptions { Path = fullPath, FullPage = fullPage });
                        return StepOutcome.Passed($"Screenshot saved to {fileName}", new Dictionary<string, object?> { ["path"] = fileName });
                    }
                case "waitForSelector":
                    {
                        var selector = parameters.GetValueOrDefault("selector") as string
                            ?? throw new ArgumentException("web.waitForSelector requires a 'selector' parameter.");
                        var timeoutMs = ToFloat(parameters.GetValueOrDefault("timeoutMs")) ?? 10000f;
                        await WithSelectorNotFound(selector, () => page.WaitForSelectorAsync(selector, new PageWaitForSelectorOptions { Timeout = timeoutMs }), ct);
                        return StepOutcome.Passed($"{selector} appeared.");
                    }
                default:
                    return StepOutcome.Failed($"Unknown web action \"{action}\".");
            }
        }
        catch (SelectorNotFoundException ex)
        {
            return StepOutcome.Failed(ex.Message);
        }
        catch (Exception ex)
        {
            return StepOutcome.Failed(ex.Message);
        }
    }

    /// <summary>Converts Playwright's own PlaywrightException (its public
    /// surface for every action/protocol failure, including a selector
    /// never becoming actionable in time — Playwright has no separate
    /// public TimeoutException; the one that exists is internal to
    /// Microsoft.Playwright.Core) into a SelectorNotFoundException that
    /// always names the selector (D3-T2) — every locator-based action that
    /// can legitimately fail to find its element goes through this, never a
    /// bare Playwright call.</summary>
    private static async Task WithSelectorNotFound(string selector, Func<Task> action, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try { await action(); }
        catch (Microsoft.Playwright.PlaywrightException ex) { throw new SelectorNotFoundException(selector, ex); }
    }

    private static async Task<T> WithSelectorNotFound<T>(string selector, Func<Task<T>> action, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try { return await action(); }
        catch (Microsoft.Playwright.PlaywrightException ex) { throw new SelectorNotFoundException(selector, ex); }
    }

    private static float? ToFloat(object? value) => value is null ? null : Convert.ToSingle(value);
}
