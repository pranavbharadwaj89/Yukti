using Microsoft.Extensions.Logging;
using Xunit;
using Yukti.Contracts;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;
using Yukti.Infrastructure.InMemory.Modules;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// Web UI Testing Module Spec, WEB-T7: the full assertion set, proven through the real WebModule
/// and FlowEngine -- never a direct Playwright call bypassing the module. assertText's own
/// equals/contains modes and assertVisible's own missing-or-hidden semantics are already covered
/// by WebModuleTests/WebModuleLocatorMigrationTests; this file covers everything new in WEB-T7:
/// matches/exact on text assertions, assertValue, assertAttribute, assertEnabled/assertDisabled,
/// assertChecked, assertCount, assertUrl, assertTitle, assertHidden, and the screenshot every
/// assertion failure now attaches.
/// </summary>
public sealed class WebAssertionTests
{
    private const string Html = """
        <!doctype html>
        <html>
        <head><title>Assertions Page</title></head>
        <body>
          <p id="greeting">  Hello,   World!  </p>
          <input id="username" value="ada lovelace" />
          <a id="link" href="https://example.com/path" data-role="external">Example</a>
          <button id="enabledBtn">Go</button>
          <button id="disabledBtn" disabled>No</button>
          <input id="checkedBox" type="checkbox" checked />
          <input id="uncheckedBox" type="checkbox" />
          <ul id="list"><li>a</li><li>b</li><li>c</li></ul>
          <div id="hiddenDiv" style="display:none">Hidden</div>
        </body>
        </html>
        """;

    private static readonly string DataUrl = "data:text/html," + Uri.EscapeDataString(Html);

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

    private static Dictionary<string, object?> P(params (string, object?)[] kv) => kv.ToDictionary(x => x.Item1, x => x.Item2);

    private static async Task<FlowRun> RunSteps(FlowEngine engine, TenantId tenantId, params (string action, Dictionary<string, object?> @params)[] steps)
    {
        var flow = Flow.CreateDraft("Web assertion test flow", null, tenantId, UserId.New());
        var navigate = ("navigate", P(("url", DataUrl)));
        foreach (var (action, @params) in new[] { navigate }.Concat(steps))
            flow.AddStep(action, ModuleKind.Web, action, @params);
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);
        return await engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);
    }

    private static void AssertAllPassed(FlowRun run)
    {
        for (var i = 0; i < run.Results.Count; i++)
            Assert.Equal(StepStatus.Passed, run.Results[i].Status);
    }

    [Fact]
    public async Task AssertText_default_mode_normalizes_whitespace()
    {
        var engine = BuildEngine();
        var run = await RunSteps(engine, TenantId.New(),
            ("assertText", P(("selector", "#greeting"), ("expected", "Hello, World!"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task AssertText_exact_mode_does_not_normalize_whitespace()
    {
        var engine = BuildEngine();
        var run = await RunSteps(engine, TenantId.New(),
            ("assertText", P(("selector", "#greeting"), ("expected", "  Hello,   World!  "), ("exact", true))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task AssertText_matches_mode_uses_a_regex()
    {
        var engine = BuildEngine();
        var run = await RunSteps(engine, TenantId.New(),
            ("assertText", P(("selector", "#greeting"), ("expected", @"^Hello, \w+!$"), ("mode", "matches"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task AssertText_failure_reports_expected_actual_and_a_screenshot()
    {
        var engine = BuildEngine();
        var run = await RunSteps(engine, TenantId.New(),
            ("assertText", P(("selector", "#greeting"), ("expected", "Goodbye"))));

        var result = run.Results[1];
        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("Goodbye", result.Error); // expected
        Assert.Contains("Hello, World!", result.Error); // actual
        var data = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(result.Data);
        var fileName = Assert.IsType<string>(data["screenshotPath"]);
        var fullPath = ScreenshotStorage.FullPath(run.Id, fileName);
        Assert.True(File.Exists(fullPath), $"Expected a failure screenshot at {fullPath}");
        File.Delete(fullPath);
    }

    [Fact]
    public async Task AssertValue_checks_an_inputs_current_value()
    {
        var engine = BuildEngine();
        var run = await RunSteps(engine, TenantId.New(),
            ("assertValue", P(("selector", "#username"), ("expected", "ada lovelace"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task AssertValue_contains_mode_works()
    {
        var engine = BuildEngine();
        var run = await RunSteps(engine, TenantId.New(),
            ("assertValue", P(("selector", "#username"), ("expected", "lovelace"), ("mode", "contains"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task AssertAttribute_checks_a_named_attribute()
    {
        var engine = BuildEngine();
        var run = await RunSteps(engine, TenantId.New(),
            ("assertAttribute", P(("selector", "#link"), ("attr", "data-role"), ("expected", "external"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task AssertAttribute_matches_mode_works()
    {
        var engine = BuildEngine();
        var run = await RunSteps(engine, TenantId.New(),
            ("assertAttribute", P(("selector", "#link"), ("attr", "href"), ("expected", @"^https://example\.com/"), ("mode", "matches"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task AssertAttribute_fails_clearly_when_the_attribute_is_absent()
    {
        var engine = BuildEngine();
        var run = await RunSteps(engine, TenantId.New(),
            ("assertAttribute", P(("selector", "#link"), ("attr", "data-missing"), ("expected", "x"))));

        var result = run.Results[1];
        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("no 'data-missing' attribute", result.Error);
    }

    [Fact]
    public async Task AssertEnabled_and_assertDisabled_check_the_right_state()
    {
        var engine = BuildEngine();
        var run = await RunSteps(engine, TenantId.New(),
            ("assertEnabled", P(("selector", "#enabledBtn"))),
            ("assertDisabled", P(("selector", "#disabledBtn"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task AssertEnabled_fails_on_a_disabled_button()
    {
        var engine = BuildEngine();
        var run = await RunSteps(engine, TenantId.New(), ("assertEnabled", P(("selector", "#disabledBtn"))));

        var result = run.Results[1];
        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("disabled", result.Error);
    }

    [Fact]
    public async Task AssertChecked_checks_both_checked_and_unchecked_states()
    {
        var engine = BuildEngine();
        var run = await RunSteps(engine, TenantId.New(),
            ("assertChecked", P(("selector", "#checkedBox"))),
            ("assertChecked", P(("selector", "#uncheckedBox"), ("checked", false))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task AssertChecked_fails_when_the_state_does_not_match()
    {
        var engine = BuildEngine();
        var run = await RunSteps(engine, TenantId.New(), ("assertChecked", P(("selector", "#uncheckedBox"))));

        var result = run.Results[1];
        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("unchecked", result.Error);
    }

    [Fact]
    public async Task AssertCount_checks_how_many_elements_match()
    {
        var engine = BuildEngine();
        var run = await RunSteps(engine, TenantId.New(), ("assertCount", P(("selector", "#list li"), ("expected", 3))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task AssertCount_fails_naming_the_real_count()
    {
        var engine = BuildEngine();
        var run = await RunSteps(engine, TenantId.New(), ("assertCount", P(("selector", "#list li"), ("expected", 5))));

        var result = run.Results[1];
        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("got 3", result.Error);
    }

    [Fact]
    public async Task AssertUrl_checks_the_current_pages_url()
    {
        var engine = BuildEngine();
        var run = await RunSteps(engine, TenantId.New(), ("assertUrl", P(("expected", "data:text/html,"), ("mode", "contains"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task AssertTitle_checks_the_current_pages_title()
    {
        var engine = BuildEngine();
        var run = await RunSteps(engine, TenantId.New(), ("assertTitle", P(("expected", "Assertions Page"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task AssertTitle_matches_mode_works()
    {
        var engine = BuildEngine();
        var run = await RunSteps(engine, TenantId.New(), ("assertTitle", P(("expected", "^Assertions"), ("mode", "matches"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task AssertHidden_passes_for_a_display_none_element()
    {
        var engine = BuildEngine();
        var run = await RunSteps(engine, TenantId.New(), ("assertHidden", P(("selector", "#hiddenDiv"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task AssertHidden_passes_for_a_missing_element_too()
    {
        var engine = BuildEngine();
        var run = await RunSteps(engine, TenantId.New(), ("assertHidden", P(("selector", "#does-not-exist"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task AssertHidden_fails_when_the_element_is_actually_visible()
    {
        var engine = BuildEngine();
        var run = await RunSteps(engine, TenantId.New(), ("assertHidden", P(("selector", "#greeting"))));

        var result = run.Results[1];
        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("not hidden", result.Error);
    }
}
