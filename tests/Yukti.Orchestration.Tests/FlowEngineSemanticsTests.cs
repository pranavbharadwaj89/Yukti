using Microsoft.Extensions.Logging;
using Xunit;
using Yukti.Application.Abstractions;
using Yukti.Contracts;
using Yukti.Domain.Events;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Orchestration.Tests;

/// <summary>Test-only module returning a scripted sequence of outcomes, one
/// per call, falling back to Passed once the script runs out. Used for both
/// the retry/flaky group (one step, several attempts) and the fail-fast/
/// ContinueOnFailure group (several steps, one call each, MaxAttempts:1).</summary>
public sealed class ScriptedModule : IAutomationModule
{
    public static readonly ModuleKind Kind = ModuleKind.Custom("scripted-test-only");
    ModuleKind IAutomationModule.Kind => Kind;
    public string ContractVersion => "1.0.0";

    private readonly Queue<StepOutcome> _script;
    public int CallCount { get; private set; }

    public ScriptedModule(params StepOutcome[] script) => _script = new Queue<StepOutcome>(script);

    public IReadOnlyList<ActionSchema> GetSupportedActions() => new[]
    {
        new ActionSchema { ActionName = "run", Description = "Returns the next scripted outcome.", Parameters = Array.Empty<ParamSpec>() },
    };

    public Task Setup(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;
    public Task Teardown(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;

    public Task<StepOutcome> Run(string action, IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct)
    {
        CallCount++;
        return Task.FromResult(_script.Count > 0 ? _script.Dequeue() : StepOutcome.Passed());
    }
}

public sealed class FlowEngineSemanticsTests
{
    private static FlowEngine BuildEngine(IAutomationModule module, RetryPolicy retryPolicy, out InMemoryDomainEventDispatcher dispatcher)
    {
        var loggerFactory = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Warning));
        dispatcher = new InMemoryDomainEventDispatcher();
        var uowFactory = new InMemoryUnitOfWorkFactory(dispatcher);
        var runRepo = new InMemoryFlowRunRepository(uowFactory);

        var registry = new ModuleRegistry();
        registry.Register(module);
        var moduleRegistrations = new InMemoryModuleRegistrationRepository(uowFactory);
        var moduleDispatcher = new ModuleDispatcher(registry, moduleRegistrations, new ModuleExecutionStrategySelector(), loggerFactory.CreateLogger<ModuleDispatcher>());
        var variableStore = new VariableStore();
        var retryHandler = new RetryFlakeHandler(loggerFactory.CreateLogger<RetryFlakeHandler>());
        return new FlowEngine(moduleDispatcher, registry, variableStore, retryHandler, runRepo, uowFactory,
            loggerFactory.CreateLogger<FlowEngine>(), retryPolicy);
    }

    private static Flow OneStepFlow(ModuleKind kind = default)
    {
        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("Retry/flaky single-step flow", null, tenantId, UserId.New());
        flow.AddStep("The only step", kind == default ? ScriptedModule.Kind : kind, "run", new Dictionary<string, object?>());
        return flow;
    }

    private static readonly RetryPolicy ZeroDelay3Attempts = new(MaxAttempts: 3, InitialBackoff: TimeSpan.Zero, BackoffMultiplier: 1.0);
    private static readonly RetryPolicy ZeroDelay1Attempt = new(MaxAttempts: 1, InitialBackoff: TimeSpan.Zero, BackoffMultiplier: 1.0);

    // ---- Retry and flaky marking ----

    [Fact]
    public async Task Fails_twice_then_passes_is_Passed_and_flaky_with_exactly_2_retry_history_entries()
    {
        var module = new ScriptedModule(StepOutcome.Failed("boom 1"), StepOutcome.Failed("boom 2"), StepOutcome.Passed());
        var engine = BuildEngine(module, ZeroDelay3Attempts, out var dispatcher);
        var flakeEvents = 0;
        dispatcher.Subscribe<FlowRunFlakeDetectedEvent>(_ => flakeEvents++);

        var flow = OneStepFlow();
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, flow.TenantId);
        var completed = await engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);

        var result = Assert.Single(completed.Results);
        Assert.Equal(StepStatus.Passed, result.Status);
        Assert.True(result.IsFlaky);
        Assert.Equal(2, result.RetryHistory.Count);
        Assert.Equal(1, result.RetryHistory[0].AttemptNumber);
        Assert.Equal(2, result.RetryHistory[1].AttemptNumber);
        Assert.All(result.RetryHistory, a => Assert.NotEqual(default, a.AttemptedAt));
        Assert.Equal("boom 1", result.RetryHistory[0].Error);
        Assert.Equal("boom 2", result.RetryHistory[1].Error);
        Assert.Equal(1, flakeEvents); // published exactly once for this step
    }

    [Fact]
    public async Task Fails_all_three_attempts_is_Failed_with_2_history_entries_plus_the_final_error_not_flaky()
    {
        var module = new ScriptedModule(StepOutcome.Failed("attempt 1"), StepOutcome.Failed("attempt 2"), StepOutcome.Failed("attempt 3, final"));
        var engine = BuildEngine(module, ZeroDelay3Attempts, out var dispatcher);
        var flakeEvents = 0;
        dispatcher.Subscribe<FlowRunFlakeDetectedEvent>(_ => flakeEvents++);

        var flow = OneStepFlow();
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, flow.TenantId);
        var completed = await engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);

        var result = Assert.Single(completed.Results);
        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.False(result.IsFlaky);
        Assert.Equal(2, result.RetryHistory.Count); // the 2 genuine retries, not the 3rd/final attempt
        Assert.Equal("attempt 3, final", result.Error); // the final attempt's error lives on the result itself
        Assert.Equal(0, flakeEvents);
    }

    [Fact]
    public async Task MaxAttempts_1_failing_once_is_Failed_with_no_retry_history()
    {
        var module = new ScriptedModule(StepOutcome.Failed("the only attempt"));
        var engine = BuildEngine(module, ZeroDelay1Attempt, out _);

        var flow = OneStepFlow();
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, flow.TenantId);
        var completed = await engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);

        var result = Assert.Single(completed.Results);
        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Empty(result.RetryHistory);
        Assert.Equal("the only attempt", result.Error);
    }

    [Fact]
    public async Task Passing_on_the_first_attempt_is_not_flaky_and_has_empty_retry_history()
    {
        var module = new ScriptedModule(StepOutcome.Passed());
        var engine = BuildEngine(module, ZeroDelay3Attempts, out var dispatcher);
        var flakeEvents = 0;
        dispatcher.Subscribe<FlowRunFlakeDetectedEvent>(_ => flakeEvents++);

        var flow = OneStepFlow();
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, flow.TenantId);
        var completed = await engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);

        var result = Assert.Single(completed.Results);
        Assert.Equal(StepStatus.Passed, result.Status);
        Assert.False(result.IsFlaky);
        Assert.Empty(result.RetryHistory);
        Assert.Equal(0, flakeEvents);
    }

    // ---- Fail-fast, ContinueOnFailure, when→Skipped ----

    [Fact]
    public async Task Default_settings_step_2_of_4_fails_stops_early_with_2_results_run_Failed()
    {
        var module = new ScriptedModule(StepOutcome.Passed(), StepOutcome.Failed("step 2 fails"), StepOutcome.Passed(), StepOutcome.Passed());
        var engine = BuildEngine(module, ZeroDelay1Attempt, out _);

        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("4-step fail-fast flow", null, tenantId, UserId.New());
        for (var i = 1; i <= 4; i++)
            flow.AddStep($"Step {i}", ScriptedModule.Kind, "run", new Dictionary<string, object?>());

        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);
        var completed = await engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);

        Assert.Equal(2, completed.Results.Count);
        Assert.Equal(StepStatus.Passed, completed.Results[0].Status);
        Assert.Equal(StepStatus.Failed, completed.Results[1].Status);
        Assert.Equal(RunStatus.Failed, completed.Status);
        Assert.Equal(2, module.CallCount); // steps 3 and 4 never dispatched
    }

    [Fact]
    public async Task ContinueOnFailure_true_step_2_of_4_fails_still_runs_all_4_run_Failed()
    {
        var module = new ScriptedModule(StepOutcome.Passed(), StepOutcome.Failed("step 2 fails"), StepOutcome.Passed(), StepOutcome.Passed());
        var engine = BuildEngine(module, ZeroDelay1Attempt, out _);

        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("4-step continue-on-failure flow", null, tenantId, UserId.New());
        flow.SetContinueOnFailure(true);
        for (var i = 1; i <= 4; i++)
            flow.AddStep($"Step {i}", ScriptedModule.Kind, "run", new Dictionary<string, object?>());

        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);
        var completed = await engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);

        Assert.Equal(4, completed.Results.Count);
        Assert.Equal(StepStatus.Passed, completed.Results[0].Status);
        Assert.Equal(StepStatus.Failed, completed.Results[1].Status);
        Assert.Equal(StepStatus.Passed, completed.Results[2].Status);
        Assert.Equal(StepStatus.Passed, completed.Results[3].Status);
        Assert.Equal(RunStatus.Failed, completed.Status); // one failure is enough, even though every step ran
        Assert.Equal(4, module.CallCount);
    }

    [Fact]
    public async Task A_false_when_condition_skips_the_step_keeps_it_in_results_and_never_calls_Run()
    {
        var module = new ScriptedModule(StepOutcome.Passed(), StepOutcome.Passed());
        var engine = BuildEngine(module, ZeroDelay1Attempt, out _);

        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("when->Skipped flow", null, tenantId, UserId.New());
        flow.AddStep("Step 1", ScriptedModule.Kind, "run", new Dictionary<string, object?>());
        flow.AddStep("Step 2 — conditionally skipped", ScriptedModule.Kind, "run", new Dictionary<string, object?>(), whenCondition: "vars.neverProduced");
        flow.AddStep("Step 3", ScriptedModule.Kind, "run", new Dictionary<string, object?>());

        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);
        var completed = await engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);

        Assert.Equal(3, completed.Results.Count); // skipped step stays in results, in its original position
        Assert.Equal(StepStatus.Passed, completed.Results[0].Status);
        Assert.Equal(StepStatus.Skipped, completed.Results[1].Status);
        Assert.Equal(StepStatus.Passed, completed.Results[2].Status);
        Assert.Equal(2, module.CallCount); // step 2's Run was never called
        Assert.Equal(RunStatus.Passed, completed.Status); // Passed + Skipped, no failures, is Passed
    }
}
