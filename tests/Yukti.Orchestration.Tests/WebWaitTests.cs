using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Xunit;
using Yukti.Contracts;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;
using Yukti.Infrastructure.InMemory.Modules;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// Web UI Testing Module Spec, WEB-T5: Selenium's implicit/explicit waits, expressed as data.
/// Every DOM mutation in the fixture HTML below is delayed by 300ms via setTimeout, so a passing
/// test genuinely proves waiting happened, not that the condition was already true.
/// </summary>
public sealed class WebWaitTests : IAsyncLifetime
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private IPage _page = null!;

    private const string Html = """
        <!doctype html>
        <html>
        <head><title>Loading</title></head>
        <body>
          <button id="btn" disabled>Go</button>
          <button id="btn2">Go2</button>
          <div id="box" style="display:none">Box</div>
          <div id="box2">Box2</div>
          <p id="msg">old</p>
          <ul id="list"></ul>
          <div id="status" data-state="pending"></div>
          <script>
            setTimeout(() => { document.getElementById('btn').disabled = false; }, 300);
            setTimeout(() => { document.getElementById('btn2').disabled = true; }, 300);
            setTimeout(() => { document.getElementById('box').style.display = 'block'; }, 300);
            setTimeout(() => { document.getElementById('box2').style.display = 'none'; }, 300);
            setTimeout(() => { document.getElementById('msg').textContent = 'ready'; }, 300);
            setTimeout(() => { document.getElementById('list').innerHTML = '<li>a</li><li>b</li>'; }, 300);
            setTimeout(() => { document.getElementById('status').setAttribute('data-state', 'done'); }, 300);
            setTimeout(() => { document.title = 'Ready now'; }, 300);
          </script>
        </body>
        </html>
        """;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        _page = await _browser.NewPageAsync();
        await _page.SetContentAsync(Html);
    }

    public async Task DisposeAsync()
    {
        await _browser.CloseAsync();
        _playwright.Dispose();
    }

    private static Yukti.Domain.UiVocabulary.Locator ById(string id, string? description = null) =>
        new(new Yukti.Domain.UiVocabulary.LocatorSpec(Yukti.Domain.UiVocabulary.LocatorStrategy.Css, "#" + id, null), Array.Empty<Yukti.Domain.UiVocabulary.LocatorSpec>(), description, null, null);

    [Fact]
    public async Task Visible_waits_for_a_hidden_element_to_appear()
    {
        var result = await PlaywrightWaitResolver.EvaluateAsync(_page, new Yukti.Domain.UiVocabulary.Visible(ById("box")), 1000, CancellationToken.None);
        Assert.True(result.Succeeded, result.FailureDetail);
    }

    [Fact]
    public async Task Hidden_waits_for_a_visible_element_to_disappear()
    {
        var result = await PlaywrightWaitResolver.EvaluateAsync(_page, new Yukti.Domain.UiVocabulary.Hidden(ById("box2")), 1000, CancellationToken.None);
        Assert.True(result.Succeeded, result.FailureDetail);
    }

    [Fact]
    public async Task Enabled_waits_for_a_disabled_button_to_become_enabled()
    {
        var result = await PlaywrightWaitResolver.EvaluateAsync(_page, new Yukti.Domain.UiVocabulary.Enabled(ById("btn")), 1000, CancellationToken.None);
        Assert.True(result.Succeeded, result.FailureDetail);
    }

    [Fact]
    public async Task Disabled_waits_for_an_enabled_button_to_become_disabled()
    {
        var result = await PlaywrightWaitResolver.EvaluateAsync(_page, new Yukti.Domain.UiVocabulary.Disabled(ById("btn2")), 1000, CancellationToken.None);
        Assert.True(result.Succeeded, result.FailureDetail);
    }

    [Fact]
    public async Task TextEquals_waits_for_the_exact_text()
    {
        var result = await PlaywrightWaitResolver.EvaluateAsync(_page, new Yukti.Domain.UiVocabulary.TextEquals(ById("msg"), "ready"), 1000, CancellationToken.None);
        Assert.True(result.Succeeded, result.FailureDetail);
    }

    [Fact]
    public async Task TextContains_waits_for_a_substring()
    {
        var result = await PlaywrightWaitResolver.EvaluateAsync(_page, new Yukti.Domain.UiVocabulary.TextContains(ById("msg"), "ead"), 1000, CancellationToken.None);
        Assert.True(result.Succeeded, result.FailureDetail);
    }

    [Fact]
    public async Task AttributeEquals_waits_for_an_attribute_to_change()
    {
        var result = await PlaywrightWaitResolver.EvaluateAsync(_page, new Yukti.Domain.UiVocabulary.AttributeEquals(ById("status"), "data-state", "done"), 1000, CancellationToken.None);
        Assert.True(result.Succeeded, result.FailureDetail);
    }

    [Fact]
    public async Task CountEquals_waits_for_elements_to_appear()
    {
        var result = await PlaywrightWaitResolver.EvaluateAsync(_page, new Yukti.Domain.UiVocabulary.CountEquals(new Yukti.Domain.UiVocabulary.Locator(
            new Yukti.Domain.UiVocabulary.LocatorSpec(Yukti.Domain.UiVocabulary.LocatorStrategy.Css, "#list li", null), Array.Empty<Yukti.Domain.UiVocabulary.LocatorSpec>(), null, null, null), 2), 1000, CancellationToken.None);
        Assert.True(result.Succeeded, result.FailureDetail);
    }

    [Fact]
    public async Task TitleContains_waits_for_the_title_to_change()
    {
        var result = await PlaywrightWaitResolver.EvaluateAsync(_page, new Yukti.Domain.UiVocabulary.TitleContains("Ready"), 1000, CancellationToken.None);
        Assert.True(result.Succeeded, result.FailureDetail);
    }

    [Fact]
    public async Task UrlContains_matches_the_current_pages_url()
    {
        var result = await PlaywrightWaitResolver.EvaluateAsync(_page, new Yukti.Domain.UiVocabulary.UrlContains("about:blank"), 300, CancellationToken.None);
        Assert.True(result.Succeeded, result.FailureDetail);
    }

    [Fact]
    public async Task NetworkIdle_succeeds_when_there_is_no_in_flight_request()
    {
        var result = await PlaywrightWaitResolver.EvaluateAsync(_page, new Yukti.Domain.UiVocabulary.NetworkIdle(TimeSpan.FromMilliseconds(500)), 2000, CancellationToken.None);
        Assert.True(result.Succeeded, result.FailureDetail);
    }

    [Fact]
    public async Task CustomPredicate_waits_for_an_arbitrary_script_to_return_true()
    {
        var result = await PlaywrightWaitResolver.EvaluateAsync(
            _page, new Yukti.Domain.UiVocabulary.CustomPredicate("() => document.getElementById('msg').textContent === 'ready'"), 1000, CancellationToken.None);
        Assert.True(result.Succeeded, result.FailureDetail);
    }

    [Fact]
    public async Task A_timeout_names_the_condition_the_last_observed_state_and_how_long_it_waited()
    {
        var result = await PlaywrightWaitResolver.EvaluateAsync(_page, new Yukti.Domain.UiVocabulary.TextEquals(ById("msg"), "never happens"), 300, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("text to equal 'never happens'", result.FailureDetail); // which condition
        Assert.Contains("text was", result.FailureDetail); // what the actual state was
        Assert.Contains("Timed out after", result.FailureDetail); // how long it waited
    }

    [Fact]
    public async Task Unknown_web_action_sleep_does_not_exist()
    {
        // WEB-T5's own rule: no 'sleep' action exists, deliberately. This proves it reads as
        // "unknown action," not as a recognized-but-unimplemented one.
        var webModule = new WebModule();
        var ctx = new ExecutionContext { RunId = FlowRunId.New(), Variables = new Dictionary<string, object?>(), Credentials = new InMemoryCredentialResolver(), RunCancellation = CancellationToken.None, TenantId = null };
        await webModule.Setup(ctx, CancellationToken.None);
        try
        {
            var outcome = await webModule.Run("sleep", new Dictionary<string, object?> { ["ms"] = 1000 }, ctx, CancellationToken.None);
            Assert.Equal(StepStatus.Failed, outcome.Status);
            Assert.Contains("Unknown web action", outcome.Error);
        }
        finally
        {
            await webModule.Teardown(ctx, CancellationToken.None);
        }
    }

    // ── Flow-level default timeout, overridable per step ──

    private static FlowEngine BuildEngine()
    {
        var loggerFactory = LoggerFactory.Create(l => l.SetMinimumLevel(LogLevel.Warning));
        var dispatcher = new InMemoryDomainEventDispatcher();
        var uowFactory = new InMemoryUnitOfWorkFactory(dispatcher);
        var runRepo = new InMemoryFlowRunRepository(uowFactory);

        var registry = new ModuleRegistry();
        registry.Register(new WebModule());
        var moduleRegistrations = new InMemoryModuleRegistrationRepository(uowFactory);
        var moduleDispatcher = new ModuleDispatcher(registry, moduleRegistrations, new ModuleExecutionStrategySelector(), loggerFactory.CreateLogger<ModuleDispatcher>());
        var variableStore = new VariableStore();
        var retryHandler = new RetryFlakeHandler(loggerFactory.CreateLogger<RetryFlakeHandler>());
        return new FlowEngine(moduleDispatcher, registry, variableStore, retryHandler, runRepo, uowFactory,
            loggerFactory.CreateLogger<FlowEngine>(), new RetryPolicy(MaxAttempts: 1, InitialBackoff: TimeSpan.Zero, BackoffMultiplier: 1.0));
    }

    private static Dictionary<string, object?> WaitForParams(Dictionary<string, object?> condition, int? timeoutMs = null)
    {
        var p = new Dictionary<string, object?> { ["condition"] = condition };
        if (timeoutMs is not null) p["timeoutMs"] = timeoutMs;
        return p;
    }

    private static readonly Dictionary<string, object?> SlowTextCondition = new()
    {
        ["type"] = "textEquals",
        ["locator"] = new Dictionary<string, object?> { ["primary"] = new Dictionary<string, object?> { ["strategy"] = "Css", ["value"] = "#msg" } },
        ["expected"] = "ready",
    };

    [Fact]
    public async Task A_flow_level_default_timeout_applies_when_a_step_sets_none()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("Default timeout flow (too short)", null, tenantId, UserId.New());
        flow.SetDefaultTimeoutMs(100); // the DOM mutation lands at 300ms -- this must lose the race
        flow.AddStep("Navigate", ModuleKind.Web, "navigate", new Dictionary<string, object?> { ["url"] = "data:text/html," + Uri.EscapeDataString(Html) });
        flow.AddStep("Wait", ModuleKind.Web, "waitFor", WaitForParams(SlowTextCondition)); // no step-level timeoutMs

        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);
        var completed = await engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);

        Assert.Equal(StepStatus.Failed, completed.Results[1].Status);
        Assert.Contains("Timed out after", completed.Results[1].Error);
    }

    [Fact]
    public async Task A_steps_own_timeoutMs_overrides_the_flows_default()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("Default timeout flow (overridden)", null, tenantId, UserId.New());
        flow.SetDefaultTimeoutMs(100); // would be too short on its own
        flow.AddStep("Navigate", ModuleKind.Web, "navigate", new Dictionary<string, object?> { ["url"] = "data:text/html," + Uri.EscapeDataString(Html) });
        flow.AddStep("Wait", ModuleKind.Web, "waitFor", WaitForParams(SlowTextCondition, timeoutMs: 1000)); // step wins

        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);
        var completed = await engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, completed.Results[1].Status);
    }
}
