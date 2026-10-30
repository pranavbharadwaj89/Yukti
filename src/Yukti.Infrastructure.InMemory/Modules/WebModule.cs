using System.Collections.Concurrent;
using Microsoft.Playwright;
using Yukti.Application.Security;
using Yukti.Contracts;
using Yukti.Domain.Execution;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Domain.UiVocabulary;
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

        /// <summary>The run's original page -- switchToMainFrame's own target when leaving a
        /// frame, and the fallback "window" if CurrentPage is ever closed out from under it.</summary>
        public required IPage Page { get; init; }

        /// <summary>WEB-T6: the active tab/window -- switchToWindow/closeWindow change this;
        /// every action below targets CurrentPage, never Page directly, so window-switching
        /// actually affects subsequent steps.</summary>
        public IPage CurrentPage { get; set; } = null!;

        /// <summary>WEB-T6: the active iframe within CurrentPage, if switchToFrame was called.
        /// Null means "operate on CurrentPage directly." Known, flagged limitation: only the
        /// plain-string selector path below is frame-aware; the richer Locator-object path
        /// (WEB-T3/T4's PlaywrightLocatorResolver) is not yet, since IFrame and IPage don't
        /// share a common locator-building interface in Playwright's own public API.</summary>
        public IFrame? CurrentFrame { get; set; }

        /// <summary>WEB-T6: every page/tab/popup opened in this run, in open order -- what
        /// switchToWindow indexes into. Index 0 is always the original Page.</summary>
        public List<IPage> OpenPages { get; } = new();

        /// <summary>WEB-T6: set by handleDialog, consumed by the next dialog that actually
        /// fires (the persistent Page.Dialog handler below reads and clears this) -- "set before
        /// the action that triggers it," per the spec's own rule. No pending action means dismiss,
        /// the same safe default Playwright itself applies when nothing handles a dialog at all.</summary>
        public (bool Accept, string? PromptText)? PendingDialogAction { get; set; }
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
            ActionName = "back",
            Description = "Navigates the shared page back in its history.",
            Parameters = Array.Empty<ParamSpec>(),
        },
        new ActionSchema
        {
            ActionName = "forward",
            Description = "Navigates the shared page forward in its history.",
            Parameters = Array.Empty<ParamSpec>(),
        },
        new ActionSchema
        {
            ActionName = "reload",
            Description = "Reloads the shared page.",
            Parameters = Array.Empty<ParamSpec>(),
        },
        new ActionSchema
        {
            ActionName = "click",
            Description = "Clicks the first element matching a selector.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "CSS or Playwright selector of the element to click. Also accepts an inline locator object ({primary, fallbacks, description, within, nth}, WEB-T3/T4) or an already-resolved Object Repository reference." },
                new ParamSpec { Name = "timeoutMs", Type = ParamType.Number, Required = false, DefaultValue = 5000, Description = "Milliseconds to wait for the element before failing." },
                new ParamSpec { Name = "modifiers", Type = ParamType.Array, Required = false, Description = "Keyboard modifiers held during the click: Alt, Control, Meta, Shift." },
                new ParamSpec { Name = "button", Type = ParamType.String, Required = false, DefaultValue = "left", Description = "left, right, or middle." },
                new ParamSpec { Name = "clickCount", Type = ParamType.Number, Required = false, DefaultValue = 1, Description = "Number of clicks (2 for a manual double-click)." },
            }
        },
        new ActionSchema
        {
            ActionName = "doubleClick",
            Description = "Double-clicks the first element matching a selector.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "CSS or Playwright selector of the element to double-click. Also accepts an inline locator object or an Object Repository reference." },
                new ParamSpec { Name = "timeoutMs", Type = ParamType.Number, Required = false, DefaultValue = 5000, Description = "Milliseconds to wait for the element before failing." },
            }
        },
        new ActionSchema
        {
            ActionName = "rightClick",
            Description = "Right-clicks the first element matching a selector.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "CSS or Playwright selector of the element to right-click. Also accepts an inline locator object or an Object Repository reference." },
                new ParamSpec { Name = "timeoutMs", Type = ParamType.Number, Required = false, DefaultValue = 5000, Description = "Milliseconds to wait for the element before failing." },
            }
        },
        new ActionSchema
        {
            ActionName = "fill",
            Description = "Fills the first element matching a selector with a value.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "CSS or Playwright selector of the input to fill. Also accepts an inline locator object ({primary, fallbacks, description, within, nth}, WEB-T3/T4) or an already-resolved Object Repository reference." },
                new ParamSpec { Name = "value", Type = ParamType.String, Required = true, Description = "Text value to type into the matched element." },
            }
        },
        new ActionSchema
        {
            ActionName = "clear",
            Description = "Clears the first element matching a selector (e.g. a text input).",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "CSS or Playwright selector of the input to clear. Also accepts an inline locator object or an Object Repository reference." },
            }
        },
        new ActionSchema
        {
            ActionName = "type",
            Description = "Types text into the first element matching a selector, one keystroke at a time -- for inputs that react to each key (autocomplete, input masks). Use 'fill' to set a value directly in one shot instead.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "CSS or Playwright selector of the input to type into. Also accepts an inline locator object or an Object Repository reference." },
                new ParamSpec { Name = "text", Type = ParamType.String, Required = true, Description = "Text to type, one keystroke at a time." },
            }
        },
        new ActionSchema
        {
            ActionName = "press",
            Description = "Presses a key or key combination on the first element matching a selector.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "CSS or Playwright selector of the element to focus first. Also accepts an inline locator object or an Object Repository reference." },
                new ParamSpec { Name = "key", Type = ParamType.String, Required = true, Description = "A key or combination, e.g. 'Enter', 'Control+A', 'Tab'." },
            }
        },
        new ActionSchema
        {
            ActionName = "check",
            Description = "Checks a checkbox or radio button matching a selector. A no-op if already checked.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "CSS or Playwright selector of the checkbox/radio. Also accepts an inline locator object or an Object Repository reference." },
            }
        },
        new ActionSchema
        {
            ActionName = "uncheck",
            Description = "Unchecks a checkbox matching a selector. A no-op if already unchecked.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "CSS or Playwright selector of the checkbox. Also accepts an inline locator object or an Object Repository reference." },
            }
        },
        new ActionSchema
        {
            ActionName = "hover",
            Description = "Hovers the mouse over the first element matching a selector.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "CSS or Playwright selector of the element to hover. Also accepts an inline locator object or an Object Repository reference." },
            }
        },
        new ActionSchema
        {
            ActionName = "dragAndDrop",
            Description = "Drags the element matching 'source' and drops it onto the element matching 'target'.",
            Parameters = new[]
            {
                new ParamSpec { Name = "source", Type = ParamType.String, Required = true, Description = "CSS or Playwright selector of the element to drag. Also accepts an inline locator object or an Object Repository reference." },
                new ParamSpec { Name = "target", Type = ParamType.String, Required = true, Description = "CSS or Playwright selector of the drop target. Also accepts an inline locator object or an Object Repository reference." },
            }
        },
        new ActionSchema
        {
            ActionName = "scrollIntoView",
            Description = "Scrolls the first element matching a selector into view.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "CSS or Playwright selector of the element to scroll into view. Also accepts an inline locator object or an Object Repository reference." },
            }
        },
        new ActionSchema
        {
            ActionName = "scrollBy",
            Description = "Scrolls the page by a pixel offset.",
            Parameters = new[]
            {
                new ParamSpec { Name = "x", Type = ParamType.Number, Required = false, DefaultValue = 0, Description = "Horizontal pixels to scroll." },
                new ParamSpec { Name = "y", Type = ParamType.Number, Required = false, DefaultValue = 0, Description = "Vertical pixels to scroll." },
            }
        },
        new ActionSchema
        {
            ActionName = "uploadFile",
            Description = "Sets one or more files on a file input matching a selector.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "CSS or Playwright selector of the <input type=file>. Also accepts an inline locator object or an Object Repository reference." },
                new ParamSpec { Name = "paths", Type = ParamType.Array, Required = true, Description = "A file path, or an array of file paths, to upload." },
            }
        },
        new ActionSchema
        {
            ActionName = "switchToFrame",
            Description = "Switches subsequent actions into the <iframe> matching a selector, until switchToMainFrame or the run ends.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "A plain CSS selector naming the <iframe> element -- an inline locator object isn't supported here yet." },
            }
        },
        new ActionSchema
        {
            ActionName = "switchToMainFrame",
            Description = "Switches back out of whichever frame switchToFrame last entered.",
            Parameters = Array.Empty<ParamSpec>(),
        },
        new ActionSchema
        {
            ActionName = "switchToWindow",
            Description = "Switches subsequent actions to a different open tab/window/popup, by index or title.",
            Parameters = new[]
            {
                new ParamSpec { Name = "index", Type = ParamType.Number, Required = false, Description = "0-based index into the windows opened so far, in open order (0 is the original window)." },
                new ParamSpec { Name = "title", Type = ParamType.String, Required = false, Description = "The exact title of the window to switch to." },
            }
        },
        new ActionSchema
        {
            ActionName = "closeWindow",
            Description = "Closes the currently active tab/window/popup.",
            Parameters = Array.Empty<ParamSpec>(),
        },
        new ActionSchema
        {
            ActionName = "handleDialog",
            Description = "Arms how the NEXT browser dialog (alert/confirm/prompt) is handled. Must be called BEFORE the action that triggers the dialog.",
            Parameters = new[]
            {
                new ParamSpec { Name = "accept", Type = ParamType.Boolean, Required = false, DefaultValue = true, Description = "true to accept/confirm the dialog, false to dismiss/cancel it." },
                new ParamSpec { Name = "promptText", Type = ParamType.String, Required = false, Description = "Text to enter before accepting a prompt() dialog." },
            }
        },
        new ActionSchema
        {
            ActionName = "assertText",
            Description = "Asserts a selector's text content, exactly or via substring containment.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "CSS or Playwright selector of the element whose text is checked. Also accepts an inline locator object ({primary, fallbacks, description, within, nth}, WEB-T3/T4) or an already-resolved Object Repository reference." },
                new ParamSpec { Name = "expected", Type = ParamType.String, Required = true, Description = "Text the element's content is compared against." },
                new ParamSpec { Name = "mode", Type = ParamType.String, Required = false, Description = "\"contains\" for substring match; omit for exact match." },
            }
        },
        new ActionSchema
        {
            ActionName = "screenshot",
            Description = "Captures a screenshot of the current page, or of one element, to local disk.",
            Parameters = new[]
            {
                new ParamSpec { Name = "path", Type = ParamType.String, Required = false, Description = "Defaults to screenshot-<timestamp>.png." },
                new ParamSpec { Name = "fullPage", Type = ParamType.Boolean, Required = false, DefaultValue = false, Description = "Captures the full scrollable page instead of just the viewport. Ignored when 'selector' is set." },
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = false, Description = "When set, captures only this element instead of the whole page. Also accepts an inline locator object or an Object Repository reference." },
            }
        },
        new ActionSchema
        {
            ActionName = "waitForSelector",
            Description = "Waits for a selector to appear.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "CSS or Playwright selector to wait for. Also accepts an inline locator object ({primary, fallbacks, description, within, nth}, WEB-T3/T4) or an already-resolved Object Repository reference." },
                new ParamSpec { Name = "timeoutMs", Type = ParamType.Number, Required = false, DefaultValue = 10000, Description = "Milliseconds to wait before failing with SelectorNotFound." },
            }
        },
        new ActionSchema
        {
            ActionName = "select",
            Description = "Selects one or more options in the first <select> element matching a selector, by value, label, or index.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "CSS or Playwright selector of the <select> element. Also accepts an inline locator object ({primary, fallbacks, description, within, nth}, WEB-T3/T4) or an already-resolved Object Repository reference." },
                new ParamSpec { Name = "value", Type = ParamType.String, Required = false, Description = "The option value to select. Exactly one of value/values/label/labels/index/indices is required." },
                new ParamSpec { Name = "values", Type = ParamType.Array, Required = false, Description = "Several option values to select (multi-select)." },
                new ParamSpec { Name = "label", Type = ParamType.String, Required = false, Description = "The option's visible label to select." },
                new ParamSpec { Name = "labels", Type = ParamType.Array, Required = false, Description = "Several option labels to select (multi-select)." },
                new ParamSpec { Name = "index", Type = ParamType.Number, Required = false, Description = "The 0-based option index to select." },
                new ParamSpec { Name = "indices", Type = ParamType.Array, Required = false, Description = "Several 0-based option indices to select (multi-select)." },
            }
        },
        new ActionSchema
        {
            ActionName = "getText",
            Description = "Reads the trimmed text content of the first element matching a selector and returns it as step data ({text}), so a later step can use it via saveAs.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "CSS or Playwright selector of the element to read. Also accepts an inline locator object ({primary, fallbacks, description, within, nth}, WEB-T3/T4) or an already-resolved Object Repository reference." },
            }
        },
        new ActionSchema
        {
            ActionName = "waitFor",
            Description = "Waits for an explicit condition to become true (WEB-T5) -- Selenium's explicit-wait " +
                "equivalent, expressed as data. There is deliberately no 'sleep' action; this is the one escape " +
                "hatch, via the 'customPredicate' condition type.",
            Parameters = new[]
            {
                new ParamSpec { Name = "condition", Type = ParamType.Object, Required = true, Description =
                    "{type, ...}. type is one of: visible, hidden, enabled, disabled, textEquals, textContains, " +
                    "attributeEquals, countEquals, urlEquals, urlContains, titleContains, networkIdle, customPredicate. " +
                    "Every type except urlEquals/urlContains/titleContains/networkIdle/customPredicate also requires a " +
                    "'locator' field (an inline locator object or an Object Repository reference)." },
                new ParamSpec { Name = "timeoutMs", Type = ParamType.Number, Required = false, DefaultValue = 10000, Description = "Milliseconds to wait before failing, naming the condition, the last observed state, and how long it waited." },
            }
        },
        new ActionSchema
        {
            ActionName = "assertVisible",
            Description = "Asserts an element matching a selector is displayed. A missing or hidden element both read as not-visible, never an exception.",
            Parameters = new[]
            {
                new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "CSS or Playwright selector of the element to check. Also accepts an inline locator object ({primary, fallbacks, description, within, nth}, WEB-T3/T4) or an already-resolved Object Repository reference." },
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
        var session = new WebSession { Playwright = playwright, Browser = browser, Context = context, Page = page, CurrentPage = page };
        session.OpenPages.Add(page);
        AttachDialogHandler(session, page);

        // WEB-T6: any new tab/window/popup opened during the run (window.open, target="_blank",
        // etc.) joins OpenPages in open order, and gets the same dialog handling every other
        // page in this run has -- so switchToWindow/handleDialog both work regardless of which
        // page a later step is looking at.
        context.Page += (_, newPage) =>
        {
            session.OpenPages.Add(newPage);
            AttachDialogHandler(session, newPage);
        };

        _sessions[ctx.RunId] = session;
    }

    /// <summary>WEB-T6: one persistent handler per page, registered once, reading
    /// session.PendingDialogAction at the moment a dialog actually fires -- handleDialog's own
    /// job is just to set that field before the action that will trigger the dialog, per the
    /// spec's own rule.</summary>
    private static void AttachDialogHandler(WebSession session, IPage page)
    {
        page.Dialog += async (_, dialog) =>
        {
            var pending = session.PendingDialogAction;
            session.PendingDialogAction = null;
            try
            {
                if (pending is { Accept: true } action)
                    await dialog.AcceptAsync(action.PromptText);
                else
                    await dialog.DismissAsync();
            }
            catch (PlaywrightException)
            {
                // The dialog may already have been handled/dismissed by the time this runs
                // (e.g. the page navigated away) -- never let a dialog-handling failure crash
                // the browser's own event loop.
            }
        };
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

        var page = session.CurrentPage;

        try
        {
            switch (action)
            {
                case "back":
                    {
                        await page.GoBackAsync();
                        return StepOutcome.Passed("Navigated back.");
                    }
                case "forward":
                    {
                        await page.GoForwardAsync();
                        return StepOutcome.Passed("Navigated forward.");
                    }
                case "reload":
                    {
                        await page.ReloadAsync();
                        return StepOutcome.Passed("Reloaded.");
                    }
                case "navigate":
                    {
                        var url = parameters.GetValueOrDefault("url") as string
                            ?? throw new ArgumentException("web.navigate requires a 'url' parameter.");
                        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                        return StepOutcome.Passed($"Navigated to {url}");
                    }
                case "click":
                    {
                        var raw = parameters.GetValueOrDefault("selector")
                            ?? throw new ArgumentException("web.click requires a 'selector' parameter.");
                        var timeoutMs = ToFloat(parameters.GetValueOrDefault("timeoutMs")) ?? 5000f;
                        var clickOptions = BuildClickOptions(parameters, timeoutMs);

                        if (raw is string selector)
                        {
                            await WithSelectorNotFound(selector, () => BuildLegacyLocator(session, selector).ClickAsync(clickOptions), ct);
                            return StepOutcome.Passed($"Clicked {selector}");
                        }

                        var (target, info, name) = await ResolveLocatorTarget(page, raw, timeoutMs, ct);
                        await target.ClickAsync(clickOptions);
                        return StepOutcome.Passed($"Clicked {name}", locatorResolution: info);
                    }
                case "fill":
                    {
                        var raw = parameters.GetValueOrDefault("selector")
                            ?? throw new ArgumentException("web.fill requires a 'selector' parameter.");
                        var value = parameters.GetValueOrDefault("value") as string
                            ?? throw new ArgumentException("web.fill requires a 'value' parameter.");

                        if (raw is string selector)
                        {
                            await WithSelectorNotFound(selector, () => BuildLegacyLocator(session, selector).FillAsync(value), ct);
                            return StepOutcome.Passed($"Filled {selector}");
                        }

                        var (target, info, name) = await ResolveLocatorTarget(page, raw, DefaultLocatorTimeoutMs, ct);
                        await target.FillAsync(value);
                        return StepOutcome.Passed($"Filled {name}", locatorResolution: info);
                    }
                case "select":
                    {
                        var raw = parameters.GetValueOrDefault("selector")
                            ?? throw new ArgumentException("web.select requires a 'selector' parameter.");

                        if (raw is string selector)
                        {
                            var description = await WithSelectorNotFound(selector, () => SelectOptions(BuildLegacyLocator(session, selector), parameters, ct), ct);
                            return StepOutcome.Passed($"Selected {description} on {selector}");
                        }

                        var (target, info, name) = await ResolveLocatorTarget(page, raw, DefaultLocatorTimeoutMs, ct);
                        var desc = await SelectOptions(target, parameters, ct);
                        return StepOutcome.Passed($"Selected {desc} on {name}", locatorResolution: info);
                    }
                case "assertText":
                    {
                        var raw = parameters.GetValueOrDefault("selector")
                            ?? throw new ArgumentException("web.assertText requires a 'selector' parameter.");
                        var expected = parameters.GetValueOrDefault("expected") as string
                            ?? throw new ArgumentException("web.assertText requires an 'expected' parameter.");
                        var mode = parameters.GetValueOrDefault("mode") as string;

                        string actual;
                        string name;
                        LocatorResolutionInfo? info = null;
                        if (raw is string selector)
                        {
                            actual = (await WithSelectorNotFound(selector, () => BuildLegacyLocator(session, selector).TextContentAsync(), ct))?.Trim() ?? "";
                            name = selector;
                        }
                        else
                        {
                            ILocator target;
                            (target, info, name) = await ResolveLocatorTarget(page, raw, DefaultLocatorTimeoutMs, ct);
                            actual = (await target.TextContentAsync())?.Trim() ?? "";
                        }

                        var matched = mode == "contains" ? actual.Contains(expected) : actual == expected;
                        return matched
                            ? StepOutcome.Passed($"Text of {name} matched.", locatorResolution: info)
                            : StepOutcome.Failed($"Expected {name} text {(mode == "contains" ? "to contain" : "to equal")} \"{expected}\", got \"{actual}\".");
                    }
                case "getText":
                    {
                        var raw = parameters.GetValueOrDefault("selector")
                            ?? throw new ArgumentException("web.getText requires a 'selector' parameter.");

                        if (raw is string selector)
                        {
                            var legacyText = (await WithSelectorNotFound(selector, () => BuildLegacyLocator(session, selector).TextContentAsync(), ct))?.Trim() ?? "";
                            return StepOutcome.Passed($"Read text of {selector}", new Dictionary<string, object?> { ["text"] = legacyText });
                        }

                        var (target, info, name) = await ResolveLocatorTarget(page, raw, DefaultLocatorTimeoutMs, ct);
                        var text = (await target.TextContentAsync())?.Trim() ?? "";
                        return StepOutcome.Passed($"Read text of {name}", new Dictionary<string, object?> { ["text"] = text }, info);
                    }
                case "assertVisible":
                    {
                        ct.ThrowIfCancellationRequested();
                        var raw = parameters.GetValueOrDefault("selector")
                            ?? throw new ArgumentException("web.assertVisible requires a 'selector' parameter.");

                        // IsVisibleAsync never waits/throws — a missing element and a hidden one
                        // both just read as false, matching MobileModule's own assertVisible
                        // semantics exactly. Preserved for the Locator path too: a total
                        // resolution failure (no strategy matched at all) also just reads as
                        // "not visible," never an exception, for the same reason.
                        if (raw is string selector)
                        {
                            var visible = await BuildLegacyLocator(session, selector).IsVisibleAsync();
                            return visible
                                ? StepOutcome.Passed($"{selector} is visible.")
                                : StepOutcome.Failed($"Element '{selector}' is not visible (missing or hidden).");
                        }

                        var locator = raw as Locator ?? LocatorParamMapper.Parse(raw);
                        var outcome = await PlaywrightLocatorResolver.ResolveAsync(page, locator, DefaultLocatorTimeoutMs, ct);
                        var displayName = locator.Description ?? "locator";
                        if (!outcome.Succeeded)
                            return StepOutcome.Failed($"Element '{displayName}' is not visible (missing or hidden). {outcome.FailureDetail}");

                        var isVisible = await outcome.Resolved!.IsVisibleAsync();
                        return isVisible
                            ? StepOutcome.Passed($"{displayName} is visible.", locatorResolution: outcome.Info)
                            : StepOutcome.Failed($"Element '{displayName}' is not visible (missing or hidden).");
                    }
                case "screenshot":
                    {
                        // D6-T5: written under ScreenshotStorage's own per-run directory, not
                        // wherever the caller's "path" happened to point — so Yukti.Api's
                        // screenshot-serving endpoint can find it again by (runId, fileName) alone.
                        var fileName = ScreenshotStorage.SafeFileName(parameters.GetValueOrDefault("path") as string, "screenshot");
                        var fullPage = parameters.GetValueOrDefault("fullPage") as bool? ?? false;
                        var fullPath = ScreenshotStorage.PrepareFullPath(ctx.RunId, fileName);

                        // WEB-T6: a 'selector' narrows the capture to one element instead of the
                        // whole page -- fullPage is meaningless together with it and is ignored
                        // in that case, same as Playwright's own ILocator.ScreenshotAsync has no
                        // FullPage option at all.
                        var rawSelector = parameters.GetValueOrDefault("selector");
                        if (rawSelector is null)
                        {
                            await page.ScreenshotAsync(new PageScreenshotOptions { Path = fullPath, FullPage = fullPage });
                        }
                        else if (rawSelector is string selector)
                        {
                            await WithSelectorNotFound(selector, () => BuildLegacyLocator(session, selector).ScreenshotAsync(new LocatorScreenshotOptions { Path = fullPath }), ct);
                        }
                        else
                        {
                            var (target, _, _) = await ResolveLocatorTarget(page, rawSelector, DefaultLocatorTimeoutMs, ct);
                            await target.ScreenshotAsync(new LocatorScreenshotOptions { Path = fullPath });
                        }

                        return StepOutcome.Passed($"Screenshot saved to {fileName}", new Dictionary<string, object?> { ["path"] = fileName });
                    }
                case "waitForSelector":
                    {
                        var raw = parameters.GetValueOrDefault("selector")
                            ?? throw new ArgumentException("web.waitForSelector requires a 'selector' parameter.");
                        var timeoutMs = ToFloat(parameters.GetValueOrDefault("timeoutMs")) ?? 10000f;

                        if (raw is string selector)
                        {
                            await WithSelectorNotFound(selector, () => session.CurrentFrame is not null
                                ? session.CurrentFrame.WaitForSelectorAsync(selector, new FrameWaitForSelectorOptions { Timeout = timeoutMs })
                                : page.WaitForSelectorAsync(selector, new PageWaitForSelectorOptions { Timeout = timeoutMs }), ct);
                            return StepOutcome.Passed($"{selector} appeared.");
                        }

                        // PlaywrightLocatorResolver.ResolveAsync already IS "wait for attached,"
                        // so the Locator path needs no separate wait call.
                        var (_, info, name) = await ResolveLocatorTarget(page, raw, timeoutMs, ct);
                        return StepOutcome.Passed($"{name} appeared.", locatorResolution: info);
                    }
                case "waitFor":
                    {
                        var rawCondition = parameters.GetValueOrDefault("condition")
                            ?? throw new ArgumentException("web.waitFor requires a 'condition' parameter.");
                        var timeoutMs = ToFloat(parameters.GetValueOrDefault("timeoutMs")) ?? 10000f;

                        var condition = WaitConditionParamMapper.Parse(rawCondition);
                        var result = await PlaywrightWaitResolver.EvaluateAsync(page, condition, timeoutMs, ct);
                        return result.Succeeded
                            ? StepOutcome.Passed($"Condition met: {result.ConditionDescription} ({result.LastObservedState}).")
                            : StepOutcome.Failed(result.FailureDetail);
                    }
                case "doubleClick":
                    {
                        var raw = parameters.GetValueOrDefault("selector")
                            ?? throw new ArgumentException("web.doubleClick requires a 'selector' parameter.");
                        var timeoutMs = ToFloat(parameters.GetValueOrDefault("timeoutMs")) ?? 5000f;

                        if (raw is string selector)
                        {
                            await WithSelectorNotFound(selector, () => BuildLegacyLocator(session, selector).DblClickAsync(new LocatorDblClickOptions { Timeout = timeoutMs }), ct);
                            return StepOutcome.Passed($"Double-clicked {selector}");
                        }

                        var (target, info, name) = await ResolveLocatorTarget(page, raw, timeoutMs, ct);
                        await target.DblClickAsync(new LocatorDblClickOptions { Timeout = timeoutMs });
                        return StepOutcome.Passed($"Double-clicked {name}", locatorResolution: info);
                    }
                case "rightClick":
                    {
                        var raw = parameters.GetValueOrDefault("selector")
                            ?? throw new ArgumentException("web.rightClick requires a 'selector' parameter.");
                        var timeoutMs = ToFloat(parameters.GetValueOrDefault("timeoutMs")) ?? 5000f;

                        if (raw is string selector)
                        {
                            await WithSelectorNotFound(selector, () => BuildLegacyLocator(session, selector).ClickAsync(new LocatorClickOptions { Button = MouseButton.Right, Timeout = timeoutMs }), ct);
                            return StepOutcome.Passed($"Right-clicked {selector}");
                        }

                        var (target, info, name) = await ResolveLocatorTarget(page, raw, timeoutMs, ct);
                        await target.ClickAsync(new LocatorClickOptions { Button = MouseButton.Right, Timeout = timeoutMs });
                        return StepOutcome.Passed($"Right-clicked {name}", locatorResolution: info);
                    }
                case "clear":
                    {
                        var raw = parameters.GetValueOrDefault("selector")
                            ?? throw new ArgumentException("web.clear requires a 'selector' parameter.");

                        if (raw is string selector)
                        {
                            await WithSelectorNotFound(selector, () => BuildLegacyLocator(session, selector).ClearAsync(), ct);
                            return StepOutcome.Passed($"Cleared {selector}");
                        }

                        var (target, info, name) = await ResolveLocatorTarget(page, raw, DefaultLocatorTimeoutMs, ct);
                        await target.ClearAsync();
                        return StepOutcome.Passed($"Cleared {name}", locatorResolution: info);
                    }
                case "type":
                    {
                        var raw = parameters.GetValueOrDefault("selector")
                            ?? throw new ArgumentException("web.type requires a 'selector' parameter.");
                        var text = parameters.GetValueOrDefault("text") as string
                            ?? throw new ArgumentException("web.type requires a 'text' parameter.");

                        // Keystroke-by-keystroke, for inputs that react to each key (autocomplete,
                        // input masks) -- unlike fill, which sets the value directly in one shot.
                        if (raw is string selector)
                        {
                            await WithSelectorNotFound(selector, () => BuildLegacyLocator(session, selector).PressSequentiallyAsync(text), ct);
                            return StepOutcome.Passed($"Typed into {selector}");
                        }

                        var (target, info, name) = await ResolveLocatorTarget(page, raw, DefaultLocatorTimeoutMs, ct);
                        await target.PressSequentiallyAsync(text);
                        return StepOutcome.Passed($"Typed into {name}", locatorResolution: info);
                    }
                case "press":
                    {
                        var raw = parameters.GetValueOrDefault("selector")
                            ?? throw new ArgumentException("web.press requires a 'selector' parameter.");
                        var key = parameters.GetValueOrDefault("key") as string
                            ?? throw new ArgumentException("web.press requires a 'key' parameter (e.g. 'Enter', 'Control+A', 'Tab').");

                        if (raw is string selector)
                        {
                            await WithSelectorNotFound(selector, () => BuildLegacyLocator(session, selector).PressAsync(key), ct);
                            return StepOutcome.Passed($"Pressed '{key}' on {selector}");
                        }

                        var (target, info, name) = await ResolveLocatorTarget(page, raw, DefaultLocatorTimeoutMs, ct);
                        await target.PressAsync(key);
                        return StepOutcome.Passed($"Pressed '{key}' on {name}", locatorResolution: info);
                    }
                case "check":
                    {
                        var raw = parameters.GetValueOrDefault("selector")
                            ?? throw new ArgumentException("web.check requires a 'selector' parameter.");

                        // CheckAsync is already idempotent (a no-op if already checked) -- Playwright's
                        // own behavior, not something this module adds.
                        if (raw is string selector)
                        {
                            await WithSelectorNotFound(selector, () => BuildLegacyLocator(session, selector).CheckAsync(), ct);
                            return StepOutcome.Passed($"Checked {selector}");
                        }

                        var (target, info, name) = await ResolveLocatorTarget(page, raw, DefaultLocatorTimeoutMs, ct);
                        await target.CheckAsync();
                        return StepOutcome.Passed($"Checked {name}", locatorResolution: info);
                    }
                case "uncheck":
                    {
                        var raw = parameters.GetValueOrDefault("selector")
                            ?? throw new ArgumentException("web.uncheck requires a 'selector' parameter.");

                        if (raw is string selector)
                        {
                            await WithSelectorNotFound(selector, () => BuildLegacyLocator(session, selector).UncheckAsync(), ct);
                            return StepOutcome.Passed($"Unchecked {selector}");
                        }

                        var (target, info, name) = await ResolveLocatorTarget(page, raw, DefaultLocatorTimeoutMs, ct);
                        await target.UncheckAsync();
                        return StepOutcome.Passed($"Unchecked {name}", locatorResolution: info);
                    }
                case "hover":
                    {
                        var raw = parameters.GetValueOrDefault("selector")
                            ?? throw new ArgumentException("web.hover requires a 'selector' parameter.");

                        if (raw is string selector)
                        {
                            await WithSelectorNotFound(selector, () => BuildLegacyLocator(session, selector).HoverAsync(), ct);
                            return StepOutcome.Passed($"Hovered {selector}");
                        }

                        var (target, info, name) = await ResolveLocatorTarget(page, raw, DefaultLocatorTimeoutMs, ct);
                        await target.HoverAsync();
                        return StepOutcome.Passed($"Hovered {name}", locatorResolution: info);
                    }
                case "dragAndDrop":
                    {
                        var rawSource = parameters.GetValueOrDefault("source")
                            ?? throw new ArgumentException("web.dragAndDrop requires a 'source' parameter.");
                        var rawTarget = parameters.GetValueOrDefault("target")
                            ?? throw new ArgumentException("web.dragAndDrop requires a 'target' parameter.");

                        var (sourceLocator, sourceInfo, sourceName) = rawSource is string sourceSelector
                            ? (BuildLegacyLocator(session, sourceSelector), (LocatorResolutionInfo?)null, sourceSelector)
                            : await ResolveLocatorTarget(page, rawSource, DefaultLocatorTimeoutMs, ct);
                        var (targetLocator, targetInfo, targetName) = rawTarget is string targetSelector
                            ? (BuildLegacyLocator(session, targetSelector), (LocatorResolutionInfo?)null, targetSelector)
                            : await ResolveLocatorTarget(page, rawTarget, DefaultLocatorTimeoutMs, ct);

                        await sourceLocator.DragToAsync(targetLocator);
                        // Only one LocatorResolution can be reported per step -- the source's
                        // fallback, if any, is the more actionable one to surface (the target is
                        // just a drop point, never itself clicked/filled/asserted on).
                        return StepOutcome.Passed($"Dragged {sourceName} to {targetName}", locatorResolution: sourceInfo ?? targetInfo);
                    }
                case "scrollIntoView":
                    {
                        var raw = parameters.GetValueOrDefault("selector")
                            ?? throw new ArgumentException("web.scrollIntoView requires a 'selector' parameter.");

                        if (raw is string selector)
                        {
                            await WithSelectorNotFound(selector, () => BuildLegacyLocator(session, selector).ScrollIntoViewIfNeededAsync(), ct);
                            return StepOutcome.Passed($"Scrolled {selector} into view");
                        }

                        var (target, info, name) = await ResolveLocatorTarget(page, raw, DefaultLocatorTimeoutMs, ct);
                        await target.ScrollIntoViewIfNeededAsync();
                        return StepOutcome.Passed($"Scrolled {name} into view", locatorResolution: info);
                    }
                case "scrollBy":
                    {
                        var x = ToFloat(parameters.GetValueOrDefault("x")) ?? 0f;
                        var y = ToFloat(parameters.GetValueOrDefault("y")) ?? 0f;
                        await page.Mouse.WheelAsync(x, y);
                        return StepOutcome.Passed($"Scrolled by ({x}, {y})");
                    }
                case "uploadFile":
                    {
                        var raw = parameters.GetValueOrDefault("selector")
                            ?? throw new ArgumentException("web.uploadFile requires a 'selector' parameter.");
                        var paths = parameters.GetValueOrDefault("paths") switch
                        {
                            string single => new[] { single },
                            IEnumerable<object?> many => many.Select(p => p?.ToString() ?? throw new ArgumentException("Each 'paths' entry must be a string.")).ToArray(),
                            _ => throw new ArgumentException("web.uploadFile requires a 'paths' parameter (a string or an array of strings)."),
                        };

                        if (raw is string selector)
                        {
                            await WithSelectorNotFound(selector, () => BuildLegacyLocator(session, selector).SetInputFilesAsync(paths), ct);
                            return StepOutcome.Passed($"Uploaded {paths.Length} file(s) to {selector}");
                        }

                        var (target, info, name) = await ResolveLocatorTarget(page, raw, DefaultLocatorTimeoutMs, ct);
                        await target.SetInputFilesAsync(paths);
                        return StepOutcome.Passed($"Uploaded {paths.Length} file(s) to {name}", locatorResolution: info);
                    }
                case "switchToFrame":
                    {
                        var raw = parameters.GetValueOrDefault("selector")
                            ?? throw new ArgumentException("web.switchToFrame requires a 'selector' parameter naming the <iframe> element.");
                        var frameSelector = raw as string
                            ?? throw new ArgumentException("web.switchToFrame's 'selector' must be a plain CSS selector naming the <iframe> -- an inline locator object isn't supported here yet.");

                        var frameElement = await WithSelectorNotFound(frameSelector, () => BuildLegacyLocator(session, frameSelector).ElementHandleAsync(), ct);
                        var frame = await frameElement.ContentFrameAsync()
                            ?? throw new ArgumentException($"Element '{frameSelector}' is not an <iframe> (no content frame).");
                        session.CurrentFrame = frame;
                        return StepOutcome.Passed($"Switched into frame {frameSelector}");
                    }
                case "switchToMainFrame":
                    {
                        session.CurrentFrame = null;
                        return StepOutcome.Passed("Switched back to the main frame.");
                    }
                case "switchToWindow":
                    {
                        var rawIndex = parameters.GetValueOrDefault("index");
                        var title = parameters.GetValueOrDefault("title") as string;

                        IPage? target = null;
                        if (rawIndex is not null)
                        {
                            var index = Convert.ToInt32(rawIndex);
                            if (index < 0 || index >= session.OpenPages.Count)
                                throw new ArgumentException($"web.switchToWindow: index {index} is out of range (0..{session.OpenPages.Count - 1}).");
                            target = session.OpenPages[index];
                        }
                        else if (title is not null)
                        {
                            foreach (var candidate in session.OpenPages)
                            {
                                if (await candidate.TitleAsync() == title) { target = candidate; break; }
                            }
                            if (target is null)
                                throw new ArgumentException($"web.switchToWindow: no open window has the title '{title}'.");
                        }
                        else
                        {
                            throw new ArgumentException("web.switchToWindow requires an 'index' or a 'title' parameter.");
                        }

                        session.CurrentPage = target;
                        session.CurrentFrame = null; // a fresh window starts at its own main frame
                        return StepOutcome.Passed($"Switched to window {(rawIndex is not null ? $"index {rawIndex}" : $"titled '{title}'")}.");
                    }
                case "closeWindow":
                    {
                        var closingCurrent = ReferenceEquals(session.CurrentPage, page);
                        await session.CurrentPage.CloseAsync();
                        session.OpenPages.Remove(session.CurrentPage);

                        // Fall back to the original page if the active window just closed itself
                        // and nothing else was switched to first -- never leave CurrentPage
                        // pointing at a closed page for the next step to trip over.
                        if (closingCurrent)
                        {
                            session.CurrentPage = session.OpenPages.Contains(session.Page) ? session.Page : session.OpenPages.FirstOrDefault() ?? session.Page;
                            session.CurrentFrame = null;
                        }
                        return StepOutcome.Passed("Closed the current window.");
                    }
                case "handleDialog":
                    {
                        var accept = parameters.GetValueOrDefault("accept") as bool? ?? true;
                        var promptText = parameters.GetValueOrDefault("promptText") as string;

                        // Set now, consumed by the next dialog that actually fires (the
                        // persistent Page.Dialog handler attached in Setup) -- must be called
                        // BEFORE the action that triggers the dialog, per the spec's own rule.
                        session.PendingDialogAction = (accept, promptText);
                        return StepOutcome.Passed($"Will {(accept ? "accept" : "dismiss")} the next dialog.");
                    }
                default:
                    return StepOutcome.Failed($"Unknown web action \"{action}\".");
            }
        }
        catch (SelectorNotFoundException ex)
        {
            // WEB-T6's own checklist: "reports failures with locator diagnostics plus a
            // screenshot." Best-effort -- a page already in a bad state (closed, crashed) just
            // means no screenshot, never a second failure masking the real one.
            var screenshotFileName = await TryCaptureFailureScreenshot(session, ctx.RunId, ct);
            return StepOutcome.Failed(ex.Message, screenshotFileName is null ? null : new Dictionary<string, object?> { ["screenshotPath"] = screenshotFileName });
        }
        catch (Exception ex)
        {
            return StepOutcome.Failed(ex.Message);
        }
    }

    private static async Task<string?> TryCaptureFailureScreenshot(WebSession session, FlowRunId runId, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var fileName = ScreenshotStorage.SafeFileName(null, "failure");
            var fullPath = ScreenshotStorage.PrepareFullPath(runId, fileName);
            await session.CurrentPage.ScreenshotAsync(new PageScreenshotOptions { Path = fullPath });
            return fileName;
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }

    /// <summary>Web UI Testing Module Spec, WEB-T4: used only for the Locator-based path, not
    /// the legacy raw-string one above (whose own call sites keep their pre-existing
    /// timeoutMs-or-default literals unchanged, by design — zero behavioural change for existing
    /// flows).</summary>
    private const float DefaultLocatorTimeoutMs = 5000f;

    /// <summary>
    /// Web UI Testing Module Spec, WEB-T4: the one place an action's "selector" param, once it's
    /// anything other than a plain string, gets turned into a real element — either an
    /// already-resolved Yukti.Domain.UiVocabulary.Locator (FlowEngine.ResolveLocatorRef already
    /// did this for a RepositoryRef, WEB-T2) or an inline locator object a flow author wrote
    /// directly in a step's params (LocatorParamMapper, WEB-T4's own new parsing). Either way,
    /// resolution and fallback-flagging go through PlaywrightLocatorResolver (WEB-T3) — this
    /// module never re-implements that logic.
    /// </summary>
    /// <summary>WEB-T6: the legacy plain-string selector path stays frame-aware -- built against
    /// the active frame when switchToFrame was called, the active page otherwise. The richer
    /// Locator-object path (ResolveLocatorTarget) intentionally isn't yet; see WebSession's own
    /// CurrentFrame doc comment for why.</summary>
    private static ILocator BuildLegacyLocator(WebSession session, string selector) =>
        session.CurrentFrame is not null ? session.CurrentFrame.Locator(selector) : session.CurrentPage.Locator(selector);

    private static async Task<(ILocator Target, LocatorResolutionInfo? Info, string Name)> ResolveLocatorTarget(
        IPage page, object raw, float timeoutMs, CancellationToken ct)
    {
        var locator = raw as Locator ?? LocatorParamMapper.Parse(raw);
        var outcome = await PlaywrightLocatorResolver.ResolveAsync(page, locator, timeoutMs, ct);
        var name = locator.Description ?? "locator";
        if (!outcome.Succeeded)
            throw new SelectorNotFoundException(name, outcome.FailureDetail!);
        return (outcome.Resolved!, outcome.Info, name);
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

    /// <summary>WEB-T6: select's own by-value (the original, unchanged shape), by-label, and
    /// by-index forms, each with a multi-select plural -- 'value' still calls Playwright's plain
    /// string overload exactly as before (zero behavior change); every other form uses
    /// SelectOptionValue, Playwright's own richer shape.</summary>
    private static Task<string> SelectOptions(ILocator target, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (parameters.GetValueOrDefault("value") is string value)
            return SelectAndDescribe(target.SelectOptionAsync(value), $"'{value}'");

        if (parameters.GetValueOrDefault("values") is IEnumerable<object?> values)
        {
            var strs = values.Select(v => v?.ToString() ?? throw new ArgumentException("Each 'values' entry must be a string.")).ToArray();
            return SelectAndDescribe(target.SelectOptionAsync(strs), string.Join(", ", strs.Select(s => $"'{s}'")));
        }

        if (parameters.GetValueOrDefault("label") is string label)
            return SelectAndDescribe(target.SelectOptionAsync(new SelectOptionValue { Label = label }), $"label '{label}'");

        if (parameters.GetValueOrDefault("labels") is IEnumerable<object?> labels)
        {
            var opts = labels.Select(l => new SelectOptionValue { Label = l?.ToString() ?? throw new ArgumentException("Each 'labels' entry must be a string.") }).ToArray();
            return SelectAndDescribe(target.SelectOptionAsync(opts), string.Join(", ", opts.Select(o => $"label '{o.Label}'")));
        }

        if (parameters.GetValueOrDefault("index") is object indexRaw)
        {
            var index = Convert.ToInt32(indexRaw);
            return SelectAndDescribe(target.SelectOptionAsync(new SelectOptionValue { Index = index }), $"index {index}");
        }

        if (parameters.GetValueOrDefault("indices") is IEnumerable<object?> indices)
        {
            var opts = indices.Select(i => new SelectOptionValue { Index = Convert.ToInt32(i) }).ToArray();
            return SelectAndDescribe(target.SelectOptionAsync(opts), string.Join(", ", opts.Select(o => $"index {o.Index}")));
        }

        throw new ArgumentException("web.select requires one of: value, values, label, labels, index, indices.");

        static async Task<string> SelectAndDescribe(Task<IReadOnlyList<string>> selecting, string description)
        {
            await selecting;
            return description;
        }
    }

    /// <summary>WEB-T6: click's own modifiers (ctrl/shift/alt/meta), button (left/right/middle),
    /// and click count -- all optional, defaulting to Playwright's own plain-left-click.</summary>
    private static LocatorClickOptions BuildClickOptions(IReadOnlyDictionary<string, object?> parameters, float timeoutMs)
    {
        var options = new LocatorClickOptions { Timeout = timeoutMs };

        if (parameters.GetValueOrDefault("button") is string buttonRaw)
        {
            if (!Enum.TryParse<MouseButton>(buttonRaw, ignoreCase: true, out var button))
                throw new ArgumentException($"web.click 'button' has unknown value '{buttonRaw}'. Supported: left, right, middle.");
            options.Button = button;
        }

        if (parameters.GetValueOrDefault("clickCount") is object clickCountRaw)
            options.ClickCount = Convert.ToInt32(clickCountRaw);

        if (parameters.GetValueOrDefault("modifiers") is IEnumerable<object?> modifiersRaw)
        {
            options.Modifiers = modifiersRaw.Select(m =>
            {
                var name = m?.ToString() ?? throw new ArgumentException("Each 'modifiers' entry must be a string.");
                return Enum.TryParse<KeyboardModifier>(name, ignoreCase: true, out var modifier)
                    ? modifier
                    : throw new ArgumentException($"web.click 'modifiers' has unknown value '{name}'. Supported: Alt, Control, Meta, Shift.");
            }).ToList();
        }

        return options;
    }
}
