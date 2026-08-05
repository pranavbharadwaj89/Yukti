using Microsoft.Extensions.Logging;
using Xunit;
using Yukti.Application.Abstractions;
using Yukti.Contracts;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// Integration evidence for FR-LOG-02, FR-LOG-03, and FR-LOG-04 — runs the
/// real FlowEngine/RetryFlakeHandler/ModuleDispatcher against an in-memory
/// capturing log sink and inspects the actual captured stream, the same
/// surface a production structured sink would receive.
/// </summary>
public sealed class FlowRunLoggingTests
{
    private const string SecretValue = "sk-live-super-secret-token-do-not-log-4471";

    private static (FlowEngine Engine, CapturingLoggerProvider Logs, ICredentialResolver Credentials) BuildEngine(
        IAutomationModule module)
    {
        var loggerFactory = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Trace));
        var capturing = new CapturingLoggerProvider();
        loggerFactory.AddProvider(capturing);

        var dispatcher = new InMemoryDomainEventDispatcher();
        var uowFactory = new InMemoryUnitOfWorkFactory(dispatcher);
        var runRepo = new InMemoryFlowRunRepository(uowFactory);

        var registry = new ModuleRegistry();
        registry.Register(module);

        var moduleRegistrations = new InMemoryModuleRegistrationRepository(uowFactory);
        var moduleDispatcher = new ModuleDispatcher(registry, moduleRegistrations, new ModuleExecutionStrategySelector(), loggerFactory.CreateLogger<ModuleDispatcher>());
        var variableStore = new VariableStore();
        var retryHandler = new RetryFlakeHandler(loggerFactory.CreateLogger<RetryFlakeHandler>());
        var engine = new FlowEngine(moduleDispatcher, registry, variableStore, retryHandler, runRepo, uowFactory,
            loggerFactory.CreateLogger<FlowEngine>());

        var credentials = new InMemoryCredentialResolver(new Dictionary<string, string>
        {
            ["target-system-api-key"] = SecretValue
        });

        return (engine, capturing, credentials);
    }

    private static (Flow Flow, FlowRun Run) BuildSingleStepFlow(ModuleKind module, string action)
    {
        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("Logging test flow", null, tenantId, UserId.New());
        flow.AddStep("Touch the target system", module, action, new Dictionary<string, object?>());
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);
        return (flow, run);
    }

    [Fact]
    public async Task Credential_value_never_appears_anywhere_in_the_log_stream()
    {
        // FR-LOG-04: adversarial log-scraping — a step that genuinely
        // resolves and uses a credential still must never leak it, at any
        // log level, in any field (message, structured state, or scope).
        var (engine, logs, credentials) = BuildEngine(new SecretTouchingModule());
        var (flow, run) = BuildSingleStepFlow(SecretTouchingModule.Kind, "call");

        await engine.Execute(flow, run, credentials, CancellationToken.None);

        Assert.NotEmpty(logs.Records);
        Assert.All(logs.Records, record =>
            Assert.All(record.AllValues, value => Assert.DoesNotContain(SecretValue, value)));
    }

    [Fact]
    public async Task Every_log_record_within_the_FlowRun_carries_FlowRunId_via_scope()
    {
        // FR-LOG-03: no call site manually threads FlowRunId through — it
        // must show up on every record purely via FlowEngine's BeginScope.
        var (engine, logs, credentials) = BuildEngine(new SecretTouchingModule());
        var (flow, run) = BuildSingleStepFlow(SecretTouchingModule.Kind, "call");

        await engine.Execute(flow, run, credentials, CancellationToken.None);

        Assert.NotEmpty(logs.Records);
        Assert.All(logs.Records, record =>
            Assert.Contains(record.AllValues, v => v == $"FlowRunId={run.Id.Value}"));
    }

    [Fact]
    public async Task Flaky_step_that_eventually_passes_logs_Warning_not_Error()
    {
        // FR-LOG-02: level semantics — a step that failed once but
        // ultimately passed is a flake (Warning), never an Error; Error is
        // reserved for a step that genuinely, finally failed.
        var (engine, logs, credentials) = BuildEngine(new FlakyModule());
        var (flow, run) = BuildSingleStepFlow(FlakyModule.Kind, "flake");

        var completed = await engine.Execute(flow, run, credentials, CancellationToken.None);

        Assert.Equal(RunStatus.Passed, completed.Status);
        Assert.DoesNotContain(logs.Records, r => r.Level == LogLevel.Error);
        Assert.Contains(logs.Records, r => r.Level == LogLevel.Warning);
    }
}
