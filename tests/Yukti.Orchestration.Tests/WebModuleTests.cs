using FixtureApp;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Xunit;
using Yukti.Application.Abstractions;
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

/// <summary>D3-T2: every WebModule action proven against the real
/// FixtureApp (D3-T1), through the real FlowEngine — never a direct
/// Playwright call bypassing the module.</summary>
public sealed class WebModuleTests : IAsyncLifetime
{
    private WebApplication _fixtureApp = null!;
    public string BaseUrl { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _fixtureApp = FixtureAppHost.Build(logPath: Path.Combine(Path.GetTempPath(), $"webmodule-test-{Guid.NewGuid():N}.log"));
        await _fixtureApp.StartAsync();
        BaseUrl = _fixtureApp.Urls.First();
    }

    public async Task DisposeAsync() => await _fixtureApp.DisposeAsync();

    private static FlowEngine BuildEngine(out WebModule webModule)
    {
        var loggerFactory = LoggerFactory.Create(l => l.SetMinimumLevel(LogLevel.Warning));
        var dispatcher = new InMemoryDomainEventDispatcher();
        var uowFactory = new InMemoryUnitOfWorkFactory(dispatcher);
        var runRepo = new InMemoryFlowRunRepository(uowFactory);

        webModule = new WebModule();
        var registry = new ModuleRegistry();
        registry.Register(webModule);
        var moduleRegistrations = new InMemoryModuleRegistrationRepository(uowFactory);
        var moduleDispatcher = new ModuleDispatcher(registry, moduleRegistrations, new ModuleExecutionStrategySelector(), loggerFactory.CreateLogger<ModuleDispatcher>());
        var variableStore = new VariableStore();
        var retryHandler = new RetryFlakeHandler(loggerFactory.CreateLogger<RetryFlakeHandler>());
        return new FlowEngine(moduleDispatcher, registry, variableStore, retryHandler, runRepo, uowFactory,
            loggerFactory.CreateLogger<FlowEngine>(), new RetryPolicy(MaxAttempts: 1, InitialBackoff: TimeSpan.Zero, BackoffMultiplier: 1.0));
    }

    private static Dictionary<string, object?> P(params (string, object?)[] kv) =>
        kv.ToDictionary(x => x.Item1, x => x.Item2);

    private static async Task<FlowRun> RunSteps(FlowEngine engine, params (string name, string action, Dictionary<string, object?> @params)[] steps)
    {
        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("Web module proof flow", null, tenantId, UserId.New());
        foreach (var (name, action, @params) in steps)
            flow.AddStep(name, ModuleKind.Web, action, @params);
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);
        return await engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);
    }

    [Fact]
    public async Task Navigate_click_fill_select_chain_logs_in_and_reaches_orders()
    {
        var engine = BuildEngine(out _);
        var completed = await RunSteps(engine,
            ("Go to login", "navigate", P(("url", $"{BaseUrl}/login"))),
            ("Fill username", "fill", P(("selector", "[data-testid=username]"), ("value", "alice"))),
            ("Fill password", "fill", P(("selector", "[data-testid=password]"), ("value", "secret"))),
            ("Choose region", "select", P(("selector", "[data-testid=login-region]"), ("value", "eu"))),
            ("Submit", "click", P(("selector", "[data-testid=login-submit]"))),
            ("Order id visible", "assertVisible", P(("selector", "[data-testid=order-id]"))));

        Assert.Equal(RunStatus.Passed, completed.Status);
        Assert.All(completed.Results, r => Assert.Equal(StepStatus.Passed, r.Status));
    }

    [Fact]
    public async Task AssertText_supports_both_contains_and_exact_modes()
    {
        var engine = BuildEngine(out _);
        var completed = await RunSteps(engine,
            ("Go to orders directly", "navigate", P(("url", $"{BaseUrl}/orders"))),
            // No session yet -> redirected to /login, whose <h1> this checks.
            ("Exact match", "assertText", P(("selector", "h1"), ("expected", "Sign in"))),
            ("Contains match", "assertText", P(("selector", "h1"), ("expected", "Sign"), ("mode", "contains"))));

        Assert.Equal(RunStatus.Passed, completed.Status);
    }

    [Fact]
    public async Task AssertVisible_is_true_for_a_real_element_and_false_for_a_hidden_one()
    {
        var engine = BuildEngine(out _);
        var completed = await RunSteps(engine,
            ("Login", "navigate", P(("url", $"{BaseUrl}/login"))),
            ("Fill u", "fill", P(("selector", "[data-testid=username]"), ("value", "a"))),
            ("Fill p", "fill", P(("selector", "[data-testid=password]"), ("value", "b"))),
            ("Submit", "click", P(("selector", "[data-testid=login-submit]"))),
            ("Visible field", "assertVisible", P(("selector", "[data-testid=order-status]"))),
            ("Hidden field", "assertVisible", P(("selector", "[data-testid=hidden-field]"))));

        Assert.Equal(StepStatus.Passed, completed.Results[4].Status);
        Assert.Equal(StepStatus.Failed, completed.Results[5].Status);
        Assert.Contains("not visible", completed.Results[5].Error);
    }

    [Fact]
    public async Task GetText_returns_the_elements_text_as_step_data()
    {
        var engine = BuildEngine(out _);
        var completed = await RunSteps(engine,
            ("Login", "navigate", P(("url", $"{BaseUrl}/login"))),
            ("Fill u", "fill", P(("selector", "[data-testid=username]"), ("value", "a"))),
            ("Fill p", "fill", P(("selector", "[data-testid=password]"), ("value", "b"))),
            ("Submit", "click", P(("selector", "[data-testid=login-submit]"))),
            ("Read order id", "getText", P(("selector", "[data-testid=order-id]"))));

        var read = completed.Results[4];
        Assert.Equal(StepStatus.Passed, read.Status);
        var data = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(read.Data);
        Assert.Matches("^[0-9a-f]{12}$", Assert.IsType<string>(data["text"]));
    }

    [Fact]
    public async Task Screenshot_is_stored_under_its_own_run_directory_with_a_sanitized_file_name()
    {
        var engine = BuildEngine(out _);
        string? writtenPath = null;
        try
        {
            // D6-T5: a caller-supplied "path" with directory segments is no longer honored verbatim
            // (ScreenshotStorage.SafeFileName strips it) — the real write always lands under
            // ScreenshotStorage's own per-run directory, findable by (runId, fileName) alone, which
            // is what Yukti.Api's screenshot-serving endpoint relies on.
            var completed = await RunSteps(engine,
                ("Go to login", "navigate", P(("url", $"{BaseUrl}/login"))),
                ("Capture", "screenshot", P(("path", "../../escape-attempt.png"))));

            Assert.Equal(StepStatus.Passed, completed.Results[1].Status);
            var data = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(completed.Results[1].Data);
            var fileName = Assert.IsType<string>(data["path"]);
            Assert.Equal("escape-attempt.png", fileName);

            writtenPath = ScreenshotStorage.FullPath(completed.Id, fileName);
            Assert.True(File.Exists(writtenPath));
        }
        finally
        {
            if (writtenPath is not null && File.Exists(writtenPath)) File.Delete(writtenPath);
        }
    }

    [Fact]
    public async Task A_missing_selector_fails_with_a_typed_error_naming_the_selector_and_the_step()
    {
        var engine = BuildEngine(out _);
        const string badSelector = "[data-testid=this-does-not-exist]";
        var completed = await RunSteps(engine,
            ("Go to login", "navigate", P(("url", $"{BaseUrl}/login"))),
            ("Click something missing", "click", P(("selector", badSelector), ("timeoutMs", 500))));

        var result = completed.Results[1];
        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Equal("Click something missing", result.StepName); // the step is named via StepResult itself
        Assert.Contains(badSelector, result.Error); // the selector is named via the typed SelectorNotFoundException
    }
}
