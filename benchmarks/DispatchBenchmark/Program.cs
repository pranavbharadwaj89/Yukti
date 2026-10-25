// D6-T8: Day 2's own last-unproven NFR (P0-3) — orchestrator overhead under
// 50ms p95. A 100-no-op-step flow runs through the real FlowEngine, same
// wiring every orchestration test already uses (InMemory repos, the real
// ModuleDispatcher/RetryFlakeHandler/VariableStore) — the only thing
// replaced is the module itself, with a stub that returns immediately.
// "Excluded by construction": since the stub does no real work, whatever
// elapses between one step's module call finishing and the next one
// starting is the engine's own bookkeeping (commit, telemetry, logging,
// variable binding, condition evaluation, interpolation) and nothing else.
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Yukti.Contracts;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;
using Yukti.Orchestration;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

const int StepCount = 100;
const double P95ThresholdMs = 50.0;

var stub = new StubModule();
var loggerFactory = LoggerFactory.Create(l => l.SetMinimumLevel(LogLevel.Warning));
var dispatcher = new InMemoryDomainEventDispatcher();
var uowFactory = new InMemoryUnitOfWorkFactory(dispatcher);
var runRepo = new InMemoryFlowRunRepository(uowFactory);
var moduleRegistrations = new InMemoryModuleRegistrationRepository(uowFactory);

var registry = new ModuleRegistry();
registry.Register(stub);
var moduleDispatcher = new ModuleDispatcher(registry, moduleRegistrations, new ModuleExecutionStrategySelector(), loggerFactory.CreateLogger<ModuleDispatcher>());
var variableStore = new VariableStore();
var retryHandler = new RetryFlakeHandler(loggerFactory.CreateLogger<RetryFlakeHandler>());
// MaxAttempts: 1 — no retry logic in the measured path; this benchmark is
// about steady-state dispatch overhead, not retry/backoff behavior.
var engine = new FlowEngine(moduleDispatcher, registry, variableStore, retryHandler, runRepo, uowFactory,
    loggerFactory.CreateLogger<FlowEngine>(), new RetryPolicy(MaxAttempts: 1, InitialBackoff: TimeSpan.Zero, BackoffMultiplier: 1.0));

var tenantId = TenantId.New();
var flow = Flow.CreateDraft("Dispatch benchmark", null, tenantId, UserId.New());
for (var i = 0; i < StepCount; i++)
    flow.AddStep($"step-{i}", StubModule.Kind, "noop", new Dictionary<string, object?>());
var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);

await engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);

var calls = stub.Calls;
if (calls.Count != StepCount)
{
    Console.Error.WriteLine($"Expected {StepCount} module calls, got {calls.Count} — benchmark invalid, not reporting a number.");
    return 1;
}

var gapsMs = new List<double>();
for (var i = 1; i < calls.Count; i++)
{
    var gapTicks = calls[i].EntryTimestamp - calls[i - 1].ExitTimestamp;
    gapsMs.Add(gapTicks * 1000.0 / Stopwatch.Frequency);
}

gapsMs.Sort();
double Percentile(double p) => gapsMs[Math.Clamp((int)Math.Ceiling(p * gapsMs.Count) - 1, 0, gapsMs.Count - 1)];

var p50 = Percentile(0.50);
var p95 = Percentile(0.95);
var p99 = Percentile(0.99);

Console.WriteLine($"Dispatch overhead over {gapsMs.Count} inter-step gaps ({StepCount} steps):");
Console.WriteLine($"  p50 = {p50:F3} ms");
Console.WriteLine($"  p95 = {p95:F3} ms");
Console.WriteLine($"  p99 = {p99:F3} ms");

if (p95 > P95ThresholdMs)
{
    Console.WriteLine($"FAIL: p95 ({p95:F3} ms) exceeds the {P95ThresholdMs} ms target (Day 2, P0-3).");
    return 1;
}

Console.WriteLine($"PASS: p95 ({p95:F3} ms) is under the {P95ThresholdMs} ms target.");
return 0;

sealed class StubModule : IAutomationModule
{
    public static readonly ModuleKind Kind = ModuleKind.Custom("benchmark-stub");
    public readonly List<(long EntryTimestamp, long ExitTimestamp)> Calls = new();

    ModuleKind IAutomationModule.Kind => Kind;
    public string ContractVersion => "1.0.0";

    public IReadOnlyList<ActionSchema> GetSupportedActions() => new[]
    {
        new ActionSchema { ActionName = "noop", Description = "Returns immediately; does no real work.", Parameters = Array.Empty<ParamSpec>() }
    };

    public Task Setup(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;
    public Task Teardown(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;

    public Task<StepOutcome> Run(string action, IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct)
    {
        var entry = Stopwatch.GetTimestamp();
        var outcome = StepOutcome.Passed();
        var exit = Stopwatch.GetTimestamp();
        Calls.Add((entry, exit));
        return Task.FromResult(outcome);
    }
}
