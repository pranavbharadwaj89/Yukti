using FixtureApp;
using Microsoft.AspNetCore.Builder;
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

namespace Yukti.Orchestration.Tests;

/// <summary>
/// Web UI Testing Module Spec, WEB-T8: browser engine selection, viewport/device/locale/
/// timezone/colour-scheme configuration, storage-state reuse, and trace/video capture on
/// failure -- proven through the real WebModule and FlowEngine. Storage-state tests use the
/// real FixtureApp (not a data: URL): Playwright's own StorageStateAsync() cannot capture
/// cookies/localStorage from a data: URL at all -- confirmed empirically while building this,
/// not assumed -- since a data: URL has no persistent, real security origin.
/// </summary>
public sealed class WebBrowserContextTests : IAsyncLifetime
{
    private WebApplication _fixtureApp = null!;
    private string _baseUrl = null!;

    public async Task InitializeAsync()
    {
        _fixtureApp = FixtureAppHost.Build(logPath: Path.Combine(Path.GetTempPath(), $"webmodule-browsercontext-test-{Guid.NewGuid():N}.log"));
        await _fixtureApp.StartAsync();
        _baseUrl = _fixtureApp.Urls.First();
    }

    public async Task DisposeAsync() => await _fixtureApp.DisposeAsync();

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

    private const string EchoPage = "data:text/html,<p id=\"info\"></p><script>document.getElementById('info').textContent = " +
        "JSON.stringify({w: window.innerWidth, h: window.innerHeight, ua: navigator.userAgent, " +
        "lang: navigator.language, tz: Intl.DateTimeFormat().resolvedOptions().timeZone, " +
        "dark: window.matchMedia('(prefers-color-scheme: dark)').matches});</script>";

    private static async Task<FlowRun> RunOneStep(FlowEngine engine, Flow flow, TenantId tenantId)
    {
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);
        return await engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);
    }

    [Fact]
    public async Task Viewport_user_agent_locale_timezone_and_color_scheme_are_all_applied()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("Browser config flow", null, tenantId, UserId.New());
        flow.SetBrowserConfig(new BrowserConfig(
            ViewportWidth: 800, ViewportHeight: 600,
            UserAgent: "YuktiTestAgent/1.0",
            Locale: "fr-FR",
            TimeZoneId: "America/New_York",
            ColorScheme: Yukti.Domain.FlowAuthoring.ColorScheme.Dark));
        flow.AddStep("Navigate", ModuleKind.Web, "navigate", P(("url", EchoPage)));
        flow.AddStep("Read", ModuleKind.Web, "getText", P(("selector", "#info")));

        var run = await RunOneStep(engine, flow, tenantId);
        Assert.All(run.Results, r => Assert.Equal(StepStatus.Passed, r.Status));

        var data = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(run.Results[1].Data);
        var json = Assert.IsType<string>(data["text"]);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(800, root.GetProperty("w").GetInt32());
        Assert.Equal(600, root.GetProperty("h").GetInt32());
        Assert.Equal("YuktiTestAgent/1.0", root.GetProperty("ua").GetString());
        Assert.Equal("fr-FR", root.GetProperty("lang").GetString());
        Assert.Equal("America/New_York", root.GetProperty("tz").GetString());
        Assert.True(root.GetProperty("dark").GetBoolean());
    }

    [Fact]
    public async Task A_device_emulation_preset_supplies_its_own_defaults()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("Device preset flow", null, tenantId, UserId.New());
        flow.SetBrowserConfig(new BrowserConfig(DeviceEmulationPreset: "iPhone 13"));
        flow.AddStep("Navigate", ModuleKind.Web, "navigate", P(("url", EchoPage)));
        flow.AddStep("Read", ModuleKind.Web, "getText", P(("selector", "#info")));

        var run = await RunOneStep(engine, flow, tenantId);
        Assert.All(run.Results, r => Assert.Equal(StepStatus.Passed, r.Status));

        var data = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(run.Results[1].Data);
        var json = Assert.IsType<string>(data["text"]);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        // iPhone 13's own preset UA always names "iPhone" -- proves the preset was actually applied.
        Assert.Contains("iPhone", doc.RootElement.GetProperty("ua").GetString());
    }

    [Fact]
    public async Task An_unknown_device_preset_fails_clearly()
    {
        // Detected at Setup, before any step runs -- an infrastructure-level failure (the
        // browser context itself never comes up), same category as a browser failing to
        // launch at all, so it propagates as a real exception rather than a Failed step.
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("Bad preset flow", null, tenantId, UserId.New());
        flow.SetBrowserConfig(new BrowserConfig(DeviceEmulationPreset: "Not A Real Device"));
        flow.AddStep("Navigate", ModuleKind.Web, "navigate", P(("url", EchoPage)));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => RunOneStep(engine, flow, tenantId));
        Assert.Contains("Not A Real Device", ex.Message);
    }

    // ── Storage-state reuse ──

    [Fact]
    public async Task A_saved_storage_state_lets_a_later_flow_skip_login()
    {
        var tenantId = TenantId.New();
        var stateName = $"test-state-{Guid.NewGuid():N}";

        // Flow 1: a real login through FixtureApp's own form -- sets a real session cookie --
        // then saves the resulting storage state under a name.
        var loginEngine = BuildEngine();
        var loginFlow = Flow.CreateDraft("Login flow", null, tenantId, UserId.New());
        loginFlow.AddStep("Navigate", ModuleKind.Web, "navigate", P(("url", $"{_baseUrl}/login")));
        loginFlow.AddStep("Submit login", ModuleKind.Web, "click", P(("selector", "[data-testid=login-submit]")));
        loginFlow.AddStep("Save state", ModuleKind.Web, "saveStorageState", P(("name", stateName)));
        var loginRun = await RunOneStep(loginEngine, loginFlow, tenantId);
        Assert.All(loginRun.Results, r => Assert.Equal(StepStatus.Passed, r.Status));

        try
        {
            // Flow 2: a brand-new run that never touches /login at all -- its BrowserConfig
            // references the saved state instead, so navigating straight to /orders (which
            // FixtureApp redirects to /login for any session-less request) must already be
            // logged in.
            var laterEngine = BuildEngine();
            var laterFlow = Flow.CreateDraft("Reuses login flow", null, tenantId, UserId.New());
            laterFlow.SetBrowserConfig(new BrowserConfig(StorageStateName: stateName));
            laterFlow.AddStep("Navigate straight to orders", ModuleKind.Web, "navigate", P(("url", $"{_baseUrl}/orders")));
            laterFlow.AddStep("Order id is visible", ModuleKind.Web, "assertVisible", P(("selector", "[data-testid=order-id]")));
            var laterRun = await RunOneStep(laterEngine, laterFlow, tenantId);

            Assert.All(laterRun.Results, r => Assert.Equal(StepStatus.Passed, r.Status));
        }
        finally
        {
            var path = BrowserStorageStateStorage.FullPath(tenantId, stateName);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task A_missing_storage_state_name_fails_clearly()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("Missing state flow", null, tenantId, UserId.New());
        var missingName = $"never-saved-{Guid.NewGuid():N}";
        flow.SetBrowserConfig(new BrowserConfig(StorageStateName: missingName));
        flow.AddStep("Navigate", ModuleKind.Web, "navigate", P(("url", $"{_baseUrl}/login")));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => RunOneStep(engine, flow, tenantId));
        Assert.Contains(missingName, ex.Message);
    }

    [Fact]
    public async Task SaveStorageState_redacts_a_plaintext_password_but_keeps_session_tokens()
    {
        var tenantId = TenantId.New();
        var stateName = $"redaction-test-{Guid.NewGuid():N}";
        var engine = BuildEngine();
        var flow = Flow.CreateDraft("Redaction flow", null, tenantId, UserId.New());
        // Real Set-Cookie headers (no executeScript needed) -- one cookie literally named
        // "password", so a real saveStorageState run has something genuine to redact.
        flow.AddStep("Navigate", ModuleKind.Web, "navigate", P(("url", $"{_baseUrl}/set-test-cookies")));
        flow.AddStep("Save state", ModuleKind.Web, "saveStorageState", P(("name", stateName)));
        var run = await RunOneStep(engine, flow, tenantId);
        Assert.All(run.Results, r => Assert.Equal(StepStatus.Passed, r.Status));

        try
        {
            var path = BrowserStorageStateStorage.FullPath(tenantId, stateName);
            var savedJson = await File.ReadAllTextAsync(path);

            Assert.DoesNotContain("plaintext-secret", savedJson); // the password was redacted
            Assert.Contains("keep-this-session-token", savedJson); // the session token was not -- that's the whole point of storage state
        }
        finally
        {
            var path = BrowserStorageStateStorage.FullPath(tenantId, stateName);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ── Trace and video capture on failure ──

    [Fact]
    public async Task A_failing_run_keeps_its_trace_file()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("Failing flow", null, tenantId, UserId.New());
        flow.AddStep("Navigate", ModuleKind.Web, "navigate", P(("url", "data:text/html,<p>hi</p>")));
        flow.AddStep("Click missing", ModuleKind.Web, "click", P(("selector", "#does-not-exist"), ("timeoutMs", 300)));

        var run = await RunOneStep(engine, flow, tenantId);
        Assert.Equal(RunStatus.Failed, run.Status);

        var traceFile = Path.Combine(ScreenshotStorage.DirectoryFor(run.Id), "trace.zip");
        Assert.True(File.Exists(traceFile), $"Expected a trace file at {traceFile}");
    }

    [Fact]
    public async Task A_passing_run_discards_its_video()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("Passing flow", null, tenantId, UserId.New());
        flow.AddStep("Navigate", ModuleKind.Web, "navigate", P(("url", "data:text/html,<p>hi</p>")));

        var run = await RunOneStep(engine, flow, tenantId);
        Assert.Equal(RunStatus.Passed, run.Status);

        var runDirectory = ScreenshotStorage.DirectoryFor(run.Id);
        Assert.False(File.Exists(Path.Combine(runDirectory, "trace.zip")));
        Assert.False(Directory.Exists(runDirectory) && Directory.EnumerateFiles(runDirectory, "*.webm").Any());
    }

    // ── Browser engine selection ──

    [Theory]
    [InlineData(Yukti.Domain.FlowAuthoring.BrowserEngine.Firefox)]
    [InlineData(Yukti.Domain.FlowAuthoring.BrowserEngine.WebKit)]
    public async Task Requesting_an_engine_whose_binary_is_not_installed_fails_with_a_clear_Playwright_error_not_a_crash(Yukti.Domain.FlowAuthoring.BrowserEngine engine)
    {
        // This environment only has the Chromium binary installed (confirmed via
        // ms-playwright's own cache directory) -- this proves the ENGINE SELECTION logic
        // dispatches to the right IBrowserType (Firefox/WebKit are reached, not silently
        // ignored in favour of Chromium) without asserting a successful launch, which would
        // need those binaries actually installed. Chromium itself (the default) is exercised
        // by every other test in this file.
        var module = new WebModule();
        var ctx = new Yukti.Contracts.ExecutionContext
        {
            RunId = FlowRunId.New(),
            Variables = new Dictionary<string, object?>(),
            Credentials = new InMemoryCredentialResolver(),
            RunCancellation = CancellationToken.None,
            TenantId = TenantId.New(),
            BrowserConfig = new BrowserConfig(Engine: engine),
        };

        var ex = await Record.ExceptionAsync(() => module.Setup(ctx, CancellationToken.None));
        Assert.NotNull(ex);
        Assert.Contains("Executable doesn't exist", ex!.Message);
    }
}
