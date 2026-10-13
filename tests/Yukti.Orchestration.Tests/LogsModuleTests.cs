using FixtureApp;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Xunit;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;
using Yukti.Infrastructure.InMemory.Modules;

namespace Yukti.Orchestration.Tests;

/// <summary>D3-T3: LogsModule.checkRules proven for pattern, severity, max
/// count and time window, against a real log file written by FixtureApp
/// (D3-T1), through the real FlowEngine.</summary>
public sealed class LogsModuleTests : IAsyncLifetime
{
    private WebApplication _fixtureApp = null!;
    private HttpClient _client = null!;
    private string _logPath = null!;

    public async Task InitializeAsync()
    {
        _logPath = Path.Combine(Path.GetTempPath(), $"logsmodule-test-{Guid.NewGuid():N}.log");
        _fixtureApp = FixtureAppHost.Build(logPath: _logPath);
        await _fixtureApp.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri(_fixtureApp.Urls.First()) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _fixtureApp.DisposeAsync();
        if (File.Exists(_logPath)) File.Delete(_logPath);
    }

    private static FlowEngine BuildEngine()
    {
        var loggerFactory = LoggerFactory.Create(l => l.SetMinimumLevel(LogLevel.Warning));
        var dispatcher = new InMemoryDomainEventDispatcher();
        var uowFactory = new InMemoryUnitOfWorkFactory(dispatcher);
        var runRepo = new InMemoryFlowRunRepository(uowFactory);

        var registry = new ModuleRegistry();
        registry.Register(new LogsModule());
        var moduleRegistrations = new InMemoryModuleRegistrationRepository(uowFactory);
        var moduleDispatcher = new ModuleDispatcher(registry, moduleRegistrations, new ModuleExecutionStrategySelector(), loggerFactory.CreateLogger<ModuleDispatcher>());
        return new FlowEngine(moduleDispatcher, registry, new VariableStore(), new RetryFlakeHandler(loggerFactory.CreateLogger<RetryFlakeHandler>()),
            runRepo, uowFactory, loggerFactory.CreateLogger<FlowEngine>(), new RetryPolicy(MaxAttempts: 1, InitialBackoff: TimeSpan.Zero, BackoffMultiplier: 1.0));
    }

    private static Dictionary<string, object?> Rule(string name, int maxAllowed, string? pattern = null, string? severity = null)
    {
        var rule = new Dictionary<string, object?> { ["name"] = name, ["maxAllowed"] = maxAllowed };
        if (pattern is not null) rule["pattern"] = pattern;
        if (severity is not null) rule["severity"] = severity;
        return rule;
    }

    private static async Task<StepResult> CheckRules(Dictionary<string, object?> parameters)
    {
        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("Logs module proof flow", null, tenantId, UserId.New());
        flow.AddStep("Check logs", ModuleKind.Logs, "checkRules", parameters);
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);
        var completed = await BuildEngine().Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);
        return Assert.Single(completed.Results);
    }

    private static List<string> MatchedLines(StepResult result, int ruleIndex = 0)
    {
        var data = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(result.Data);
        var matches = Assert.IsAssignableFrom<IEnumerable<object?>>(data["matches"]).ToList();
        var match = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(matches[ruleIndex]);
        return Assert.IsAssignableFrom<IEnumerable<string>>(match["lines"]).ToList();
    }

    [Fact]
    public async Task A_pattern_rule_matches_real_fixture_log_lines_and_returns_them_in_the_result()
    {
        await _client.GetAsync("/api/orders/pat-1");
        await _client.GetAsync("/api/orders/pat-1");
        await _client.GetAsync("/api/orders/other-9");

        var result = await CheckRules(new Dictionary<string, object?>
        {
            ["logFile"] = _logPath,
            ["rules"] = new List<object?> { Rule("pat-1 requests", maxAllowed: 1, pattern: "orderId=pat-1 ") }
        });

        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("2 matches, max allowed 1", result.Error);
        var lines = MatchedLines(result);
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Contains("orderId=pat-1 ", l));
    }

    [Fact]
    public async Task A_severity_rule_counts_only_lines_at_that_level()
    {
        await _client.GetAsync("/api/orders/sev-1");
        await _client.GetAsync("/api/orders/sev-1?fail=1");

        var errorsOnly = await CheckRules(new Dictionary<string, object?>
        {
            ["logFile"] = _logPath,
            ["rules"] = new List<object?> { Rule("no errors for sev-1", maxAllowed: 0, pattern: "orderId=sev-1 ", severity: "ERROR") }
        });

        Assert.Equal(StepStatus.Failed, errorsOnly.Status);
        var line = Assert.Single(MatchedLines(errorsOnly));
        Assert.Contains(" ERROR ", line);

        var infoOnly = await CheckRules(new Dictionary<string, object?>
        {
            ["logFile"] = _logPath,
            ["rules"] = new List<object?> { Rule("one info line for sev-1", maxAllowed: 1, pattern: "orderId=sev-1 ", severity: "INFO") }
        });

        Assert.Equal(StepStatus.Passed, infoOnly.Status);
        Assert.Contains(" INFO ", Assert.Single(MatchedLines(infoOnly)));
    }

    [Fact]
    public async Task Max_count_passes_at_exactly_the_limit_and_fails_one_over_it()
    {
        await _client.GetAsync("/api/orders/max-1");
        await _client.GetAsync("/api/orders/max-1");
        await _client.GetAsync("/api/orders/max-1");

        var atLimit = await CheckRules(new Dictionary<string, object?>
        {
            ["logFile"] = _logPath,
            ["rules"] = new List<object?> { Rule("max-1", maxAllowed: 3, pattern: "orderId=max-1 ") }
        });
        var overLimit = await CheckRules(new Dictionary<string, object?>
        {
            ["logFile"] = _logPath,
            ["rules"] = new List<object?> { Rule("max-1", maxAllowed: 2, pattern: "orderId=max-1 ") }
        });

        Assert.Equal(StepStatus.Passed, atLimit.Status);
        Assert.Equal(StepStatus.Failed, overLimit.Status);
        Assert.Contains("3 matches, max allowed 2", overLimit.Error);
    }

    [Fact]
    public async Task A_time_window_excludes_lines_outside_since_and_until()
    {
        var path = Path.Combine(Path.GetTempPath(), $"logsmodule-window-{Guid.NewGuid():N}.log");
        await File.WriteAllLinesAsync(path, new[]
        {
            "2026-10-13T10:00:00.0000000+00:00 ERROR orderId=w-1 before the window",
            "2026-10-13T10:05:00.0000000+00:00 ERROR orderId=w-1 inside the window",
            "2026-10-13T10:10:00.0000000+00:00 ERROR orderId=w-1 after the window",
            "a line with no timestamp ERROR"
        });

        try
        {
            var result = await CheckRules(new Dictionary<string, object?>
            {
                ["logFile"] = path,
                ["since"] = "2026-10-13T10:03:00Z",
                ["until"] = "2026-10-13T10:07:00Z",
                ["rules"] = new List<object?> { Rule("errors in window", maxAllowed: 0, severity: "ERROR") }
            });

            Assert.Equal(StepStatus.Failed, result.Status);
            var line = Assert.Single(MatchedLines(result));
            Assert.Contains("inside the window", line);

            var data = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(result.Data);
            Assert.Equal(1, data["linesScanned"]);
            Assert.Equal(4, data["linesTotal"]);
            Assert.Equal(1, data["linesWithoutTimestamp"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A_missing_log_file_fails_the_step_naming_the_path_never_passes()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.log");

        var result = await CheckRules(new Dictionary<string, object?>
        {
            ["logFile"] = missing,
            // A rule that would trivially pass on an empty log — the old
            // behaviour treated a missing file as "0 lines, all rules passed".
            ["rules"] = new List<object?> { Rule("no errors", maxAllowed: 0, severity: "ERROR") }
        });

        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("Log file not found", result.Error);
        Assert.Contains(missing, result.Error);
    }

    [Fact]
    public async Task Neither_a_log_file_nor_log_text_fails_clearly()
    {
        var result = await CheckRules(new Dictionary<string, object?>
        {
            ["rules"] = new List<object?> { Rule("no errors", maxAllowed: 0, severity: "ERROR") }
        });

        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("requires either a 'logFile' or a 'logText'", result.Error);
    }
}
