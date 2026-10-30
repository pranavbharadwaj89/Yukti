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
using Yukti.Domain.UiVocabulary;
using Yukti.Infrastructure.InMemory;
using Yukti.Infrastructure.InMemory.Modules;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// Web UI Testing Module Spec, WEB-T4: WebModule's existing actions migrated to accept a
/// LocatorRef, through the real module and FlowEngine -- never a direct Playwright call
/// bypassing the module, same convention WebModuleTests itself documents. The pre-existing
/// WebModuleTests (6/6, unmodified by this change) already is the "a pre-migration flow
/// definition keeps working unchanged" proof the spec's own checklist asks for.
/// </summary>
public sealed class WebModuleLocatorMigrationTests : IAsyncLifetime
{
    private WebApplication _fixtureApp = null!;
    public string BaseUrl { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _fixtureApp = FixtureAppHost.Build(logPath: Path.Combine(Path.GetTempPath(), $"webmodule-locator-test-{Guid.NewGuid():N}.log"));
        await _fixtureApp.StartAsync();
        BaseUrl = _fixtureApp.Urls.First();
    }

    public async Task DisposeAsync() => await _fixtureApp.DisposeAsync();

    private static FlowEngine BuildEngine(InMemoryObjectRepositoryRepository? repositories = null)
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
        var elements = repositories is null ? null : new InMemoryObjectRepositoryElementQuery(repositories);
        return new FlowEngine(moduleDispatcher, registry, variableStore, retryHandler, runRepo, uowFactory,
            loggerFactory.CreateLogger<FlowEngine>(), new RetryPolicy(MaxAttempts: 1, InitialBackoff: TimeSpan.Zero, BackoffMultiplier: 1.0),
            repositoryElements: elements);
    }

    /// <summary>Every step runs in the SAME flow/run -- WebModule's session (browser/page) is
    /// per-run (Setup/Teardown), so a navigate and a later click must share one run or the click
    /// sees a blank page, not wherever the navigate landed.</summary>
    private static async Task<FlowRun> RunSteps(FlowEngine engine, TenantId tenantId, params (string action, Dictionary<string, object?> @params)[] steps)
    {
        var flow = Flow.CreateDraft("Locator migration flow", null, tenantId, UserId.New());
        foreach (var (action, @params) in steps)
            flow.AddStep(action, ModuleKind.Web, action, @params);
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);
        return await engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);
    }

    [Fact]
    public async Task Click_accepts_an_inline_locator_object_instead_of_a_raw_selector_string()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();

        // Inline locator, authored directly in the step's own params -- no repository involved.
        var inlineLocator = new Dictionary<string, object?>
        {
            ["primary"] = new Dictionary<string, object?> { ["strategy"] = "Css", ["value"] = "[data-testid=login-submit]" },
            ["description"] = "login submit button",
        };
        var completed = await RunSteps(engine, tenantId,
            ("navigate", new() { ["url"] = $"{BaseUrl}/login" }),
            ("click", new() { ["selector"] = inlineLocator }));

        Assert.All(completed.Results, r => Assert.Equal(StepStatus.Passed, r.Status));
        Assert.Null(completed.Results[1].LocatorResolution); // primary resolved directly -- never flagged
    }

    [Fact]
    public async Task Click_falls_back_when_the_primary_strategy_fails_and_flags_the_pass()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();

        var inlineLocator = new Dictionary<string, object?>
        {
            ["primary"] = new Dictionary<string, object?> { ["strategy"] = "Css", ["value"] = "#does-not-exist" },
            ["fallbacks"] = new List<object?> { new Dictionary<string, object?> { ["strategy"] = "Css", ["value"] = "[data-testid=login-submit]" } },
            ["description"] = "login submit button",
        };
        var completed = await RunSteps(engine, tenantId,
            ("navigate", new() { ["url"] = $"{BaseUrl}/login" }),
            ("click", new() { ["selector"] = inlineLocator }));

        var result = completed.Results[1];
        Assert.Equal(StepStatus.Passed, result.Status);
        Assert.True(result.UsedLocatorFallback);
        Assert.Equal(LocatorStrategy.Css, result.LocatorResolution!.StrategyUsed);
        Assert.Equal("login submit button", result.LocatorResolution.Description);
    }

    [Fact]
    public async Task Click_reports_full_diagnostics_when_every_strategy_fails()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();

        var inlineLocator = new Dictionary<string, object?>
        {
            ["primary"] = new Dictionary<string, object?> { ["strategy"] = "Css", ["value"] = "#does-not-exist" },
            ["description"] = "a button that isn't on this page",
        };
        var completed = await RunSteps(engine, tenantId,
            ("navigate", new() { ["url"] = $"{BaseUrl}/login" }),
            ("click", new() { ["selector"] = inlineLocator }));

        var result = completed.Results[1];
        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("matched 0 element(s)", result.Error);
        Assert.Contains("#does-not-exist", result.Error);

        // WEB-T6's own checklist: a locator-resolution failure also attaches a screenshot.
        var data = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(result.Data);
        var screenshotFileName = Assert.IsType<string>(data["screenshotPath"]);
        var fullPath = Yukti.Infrastructure.InMemory.Modules.ScreenshotStorage.FullPath(completed.Id, screenshotFileName);
        Assert.True(File.Exists(fullPath), $"Expected a failure screenshot at {fullPath}");
        File.Delete(fullPath);
    }

    [Fact]
    public async Task AssertVisible_reads_false_not_an_exception_when_an_inline_locator_resolves_to_nothing()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();

        var inlineLocator = new Dictionary<string, object?>
        {
            ["primary"] = new Dictionary<string, object?> { ["strategy"] = "Css", ["value"] = "#does-not-exist" },
        };
        var completed = await RunSteps(engine, tenantId,
            ("navigate", new() { ["url"] = $"{BaseUrl}/login" }),
            ("assertVisible", new() { ["selector"] = inlineLocator }));

        var result = completed.Results[1];
        Assert.Equal(StepStatus.Failed, result.Status); // a clean assertion failure, not an unhandled exception
        Assert.Contains("is not visible", result.Error);
    }

    [Fact]
    public async Task A_step_referencing_an_Object_Repository_entry_resolves_and_clicks_through_the_real_module()
    {
        var uowFactory = new InMemoryUnitOfWorkFactory(new InMemoryDomainEventDispatcher());
        var repositories = new InMemoryObjectRepositoryRepository(uowFactory);
        var engine = BuildEngine(repositories);
        var tenantId = TenantId.New();

        var repository = ObjectRepository.Create("Login page", tenantId);
        repository.SetElement("Login page", "submitButton",
            new Locator(new LocatorSpec(LocatorStrategy.Css, "[data-testid=login-submit]", null), Array.Empty<LocatorSpec>(), "login submit button", null, null));
        await repositories.Save(repository, CancellationToken.None);
        await uowFactory.Create().Commit(CancellationToken.None); // Save() only stages

        // FlowEngine.ResolveLocatorRef (WEB-T2) already turns this into a real Locator before
        // WebModule ever sees it -- the module's own path here is identical to the inline-locator
        // tests above, proving both authoring styles converge on the same resolver.
        var repositoryRef = new Dictionary<string, object?> { ["repositoryPage"] = "Login page", ["repositoryElement"] = "submitButton" };
        var completed = await RunSteps(engine, tenantId,
            ("navigate", new() { ["url"] = $"{BaseUrl}/login" }),
            ("click", new() { ["selector"] = repositoryRef }));

        Assert.All(completed.Results, r => Assert.Equal(StepStatus.Passed, r.Status));
    }
}
