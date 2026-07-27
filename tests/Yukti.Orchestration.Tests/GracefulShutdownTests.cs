using Microsoft.Extensions.Logging;
using Xunit;
using Yukti.Application.Abstractions;
using Yukti.Contracts;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// A module whose Run signals a TaskCompletionSource the instant it starts
/// (so the test knows execution is genuinely in-flight, not guessing via a
/// sleep), then blocks until the test releases it — the deterministic
/// stand-in for "a real step that's mid-flight when SIGTERM arrives."
/// </summary>
public sealed class ControllableSlowModule : IAutomationModule
{
    public static readonly ModuleKind Kind = ModuleKind.Custom("controllable-slow");
    ModuleKind IAutomationModule.Kind => Kind;
    public string ContractVersion => "1.0.0";

    public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => _release.TrySetResult();

    public IReadOnlyList<ActionSchema> GetSupportedActions() => new[]
    {
        new ActionSchema { ActionName = "run", Description = "Signals start, then waits to be released.", Parameters = Array.Empty<ParamSpec>() }
    };

    public Task Setup(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;
    public Task Teardown(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;

    public async Task<StepOutcome> Run(string action, IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct)
    {
        Started.TrySetResult();
        await _release.Task; // deliberately ignores ct — models a step already past its own point of no return
        return StepOutcome.Passed("finished despite shutdown having been requested mid-flight");
    }
}

/// <summary>
/// FR-OPS-02's real acceptance criterion — "SIGTERM during step execution:
/// current step completes and commits before the process exits" — tested
/// at the mechanism FlowEngine actually implements it with: step-boundary-
/// only cancellation checks (ct.ThrowIfCancellationRequested() once per
/// loop iteration, never mid-step). A literal OS SIGTERM isn't available
/// to simulate here (this suite runs on Windows, where the graceful-
/// shutdown signal is CTRL_CLOSE_EVENT/taskkill, not POSIX SIGTERM; on
/// Linux/containers, ASP.NET Core's generic host wires the real SIGTERM to
/// the same IHostApplicationLifetime.ApplicationStopping token this test
/// cancels directly) — cancelling the token FlowEngine.Execute receives,
/// at the same point in the step lifecycle SIGTERM would arrive, is the
/// faithful in-process equivalent.
///
/// Caveat, checked directly: InMemoryUnitOfWorkFactory never observes the
/// CancellationToken it's given (no real I/O to cancel), so this test
/// passes identically whether FlowEngine's in-loop commits use `ct` or
/// `CancellationToken.None` — it proves the structural contract (a
/// step's outcome is committed unconditionally once decided, never
/// re-gated on cancellation state) but not that the fix matters at
/// runtime. That part rests on EF Core's own documented contract:
/// DbContextSaveChangesAsync(CancellationToken) checks the token
/// immediately on entry and throws before issuing any SQL if it's already
/// cancelled — which is exactly why the real (non-test) commit call must
/// use CancellationToken.None here, not `ct`.
/// </summary>
public sealed class GracefulShutdownTests
{
    private static (FlowEngine Engine, IFlowRunRepository Runs) BuildEngine(IAutomationModule module)
    {
        var loggerFactory = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Warning));
        var dispatcher = new InMemoryDomainEventDispatcher();
        var uowFactory = new InMemoryUnitOfWorkFactory(dispatcher);
        var runRepo = new InMemoryFlowRunRepository(uowFactory);

        var registry = new ModuleRegistry();
        registry.Register(module);
        var moduleRegistrations = new InMemoryModuleRegistrationRepository(uowFactory);
        var moduleDispatcher = new ModuleDispatcher(registry, moduleRegistrations, new ModuleExecutionStrategySelector(), loggerFactory.CreateLogger<ModuleDispatcher>());
        var variableStore = new VariableStore();
        var retryHandler = new RetryFlakeHandler(loggerFactory.CreateLogger<RetryFlakeHandler>());
        var engine = new FlowEngine(moduleDispatcher, variableStore, retryHandler, runRepo, uowFactory,
            loggerFactory.CreateLogger<FlowEngine>(),
            // No retries — a retry loop re-checking ct would otherwise
            // confound "did the step's own commit survive cancellation"
            // with "did a retry attempt get cancelled instead."
            new RetryPolicy(MaxAttempts: 1, InitialBackoff: TimeSpan.Zero, BackoffMultiplier: 1.0));

        return (engine, runRepo);
    }

    [Fact]
    public async Task Step_in_flight_when_shutdown_is_requested_still_commits_its_result()
    {
        var slowModule = new ControllableSlowModule();
        var (engine, runs) = BuildEngine(slowModule);

        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("Graceful shutdown test flow", null, tenantId, UserId.New());
        flow.AddStep("Slow step", ControllableSlowModule.Kind, "run", new Dictionary<string, object?>());
        flow.AddStep("Never reached", ControllableSlowModule.Kind, "run", new Dictionary<string, object?>());
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);

        var cts = new CancellationTokenSource();
        var credentials = new InMemoryCredentialResolver();
        var executeTask = engine.Execute(flow, run, credentials, cts.Token);

        // Waits for the module to actually be mid-step (not a sleep-based
        // guess) before firing the "SIGTERM," matching the FR's own
        // phrasing precisely: cancellation requested *during* step execution.
        await slowModule.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        slowModule.Release();

        // FlowEngine.Execute throws OperationCanceledException out of the
        // *next* loop iteration's ThrowIfCancellationRequested() — it never
        // returns a completed FlowRun in this scenario. That's the correct
        // shape of "stops before dispatching the next step": the caller
        // (Yukti.Api's /runs endpoint) sees the request end via
        // cancellation, exactly as an aborted HTTP request would.
        await Assert.ThrowsAsync<OperationCanceledException>(() => executeTask);

        // The in-flight step still ran to completion and is durably
        // committed — the FR's actual guarantee — checked via the
        // repository, not Execute()'s return value, since it never returns.
        var persisted = await runs.GetById(run.Id, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Single(persisted!.Results); // exactly the in-flight step, not the second one
        Assert.Equal(StepStatus.Passed, persisted.Results[0].Status);
        Assert.NotEqual(RunStatus.Passed, persisted.Status); // never reached run.Complete()
    }
}
