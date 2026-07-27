using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Yukti.Application.Abstractions;
using Yukti.Application.Execution;
using Yukti.Application.IdentityAccess;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure;
using Yukti.Infrastructure.InMemory;

// FR-OPS-03: isolates "step-dispatch latency" from full synchronous flow
// execution. Yukti.Api's /runs endpoint conflates trigger + dispatch +
// execute + respond into one HTTP call (a documented temporary
// simplification — see its own Program.cs comment), so there is no HTTP
// endpoint that reports dispatch latency alone. This calls the same
// TriggerFlowRunCommandHandler that endpoint uses directly — its
// responsibility ends at "the run now durably exists" (see the handler's
// own doc comment), which is exactly the dispatch step FR-OPS-03 measures.
// Runs against the real CockroachDB connection, not an in-memory stand-in:
// dispatch latency under load is meaningless without real DB round trips.

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: Yukti.LoadTest <flowId> <tenantId> <userId> [concurrency=500]");
    return 1;
}

var flowId = new FlowId(Guid.Parse(args[0]));
var tenantId = new TenantId(Guid.Parse(args[1]));
var userId = new UserId(Guid.Parse(args[2]));
var concurrency = args.Length > 3 ? int.Parse(args[3]) : 500;

var configuration = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .AddEnvironmentVariables()
    .Build();
var baseConnectionString = configuration.GetConnectionString("YuktiRuntime")
    ?? configuration.GetConnectionString("Yukti")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:YuktiRuntime/Yukti user secret.");
// Npgsql's default Maximum Pool Size (100) would otherwise queue most of a
// 500-concurrent burst behind pool exhaustion, measuring queueing delay
// instead of the dispatch latency this benchmark exists to isolate — sized
// to the requested concurrency instead, same as a real deployment would
// size its pool to its expected concurrent load.
var connectionString = $"{baseConnectionString};Maximum Pool Size={Math.Max(100, concurrency + 20)}";

var services = new ServiceCollection();
services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
services.AddYuktiInfrastructure(connectionString);
services.AddSingleton<InMemoryDomainEventDispatcher>();
services.AddSingleton<IDomainEventDispatcher>(sp => sp.GetRequiredService<InMemoryDomainEventDispatcher>());
services.AddScoped<ITenantContextAccessor>(_ => new FixedTenantContextAccessor(tenantId));
services.AddScoped<IPermissionChecker, PermissionChecker>();
services.AddScoped<TriggerFlowRunCommandHandler>();
services.AddScoped<ITenantSessionInitializer, EfTenantSessionInitializer>();

await using var provider = services.BuildServiceProvider();

Console.WriteLine($"FR-OPS-03 dispatch-latency benchmark — {concurrency} concurrent TriggerFlowRunCommand dispatches");

// Warms the Npgsql connection pool before the timed run — ALL `concurrency`
// connections, not a fixed small sample: a fresh physical connection's TLS
// handshake to CockroachDB Cloud (ap-south-1, a real cross-region round
// trip — measured directly at ~250-330ms, versus ~24ms for a query on an
// already-open connection) dominates any request unlucky enough to draw a
// still-cold connection from the pool. Warming fewer than `concurrency`
// connections here just means the timed run below pays that handshake cost
// instead, which is exactly the confound this warmup exists to remove —
// the same way a production deployment's pool is already warm by the time
// it takes real traffic, not paying per-connection handshake cost on the
// critical path of a live request.
await Task.WhenAll(Enumerable.Range(0, concurrency).Select(async _ =>
{
    using var warmupScope = provider.CreateScope();
    var warmupInit = warmupScope.ServiceProvider.GetRequiredService<ITenantSessionInitializer>();
    await warmupInit.EstablishTenantContext(tenantId, CancellationToken.None);
}));

var latenciesMs = new double[concurrency];
var errors = 0;

var tasks = Enumerable.Range(0, concurrency).Select(async i =>
{
    using var scope = provider.CreateScope();
    var sessionInit = scope.ServiceProvider.GetRequiredService<ITenantSessionInitializer>();
    await sessionInit.EstablishTenantContext(tenantId, CancellationToken.None);
    var handler = scope.ServiceProvider.GetRequiredService<TriggerFlowRunCommandHandler>();
    var sw = Stopwatch.StartNew();
    try
    {
        await handler.Handle(new TriggerFlowRunCommand(flowId, RunTrigger.Api, null, tenantId, userId), CancellationToken.None);
        latenciesMs[i] = sw.Elapsed.TotalMilliseconds;
    }
    catch (Exception ex)
    {
        Interlocked.Increment(ref errors);
        Console.Error.WriteLine($"[{i}] failed: {ex.Message} | inner: {ex.InnerException?.Message}");
        latenciesMs[i] = double.NaN;
    }
});

var wallClock = Stopwatch.StartNew();
await Task.WhenAll(tasks);
wallClock.Stop();

var ok = latenciesMs.Where(l => !double.IsNaN(l)).OrderBy(l => l).ToArray();
double Percentile(double p) => ok.Length == 0 ? double.NaN : ok[Math.Min(ok.Length - 1, (int)Math.Ceiling(p / 100.0 * ok.Length) - 1)];

Console.WriteLine($"Completed: {ok.Length}/{concurrency} succeeded, {errors} failed, wall clock {wallClock.Elapsed.TotalSeconds:F2}s");
Console.WriteLine($"p50={Percentile(50):F1}ms p90={Percentile(90):F1}ms p95={Percentile(95):F1}ms p99={Percentile(99):F1}ms max={(ok.Length > 0 ? ok[^1] : double.NaN):F1}ms");

return 0;

sealed class FixedTenantContextAccessor : ITenantContextAccessor
{
    public FixedTenantContextAccessor(TenantId tenantId) => CurrentTenantId = tenantId;
    public TenantId? CurrentTenantId { get; }
}
