using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using Yukti.Application.Abstractions;
using Yukti.Application.Execution;
using Yukti.Application.IdentityAccess;
using Yukti.Domain.Events;
using Yukti.Infrastructure;
using Yukti.Infrastructure.InMemory;
using Yukti.Infrastructure.ReadModels;
using Yukti.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Same JSON structured-logging shape as Yukti.Api (FR-LOG) — one process,
// one log format, regardless of which container it's read from.
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
});

// FR-AUDIT-03/FR-OPS-01 fallout: this process hosts exactly the
// background/cross-tenant work yukti_worker (BYPASSRLS) exists for — the
// Scheduler fires triggers across every tenant, the outbox relay processes
// every tenant's events, and the trend job aggregates across every tenant.
// Falls back to the plain connection string in environments that haven't
// set up the split roles yet, same as Yukti.Api.
var connectionString = builder.Configuration.GetConnectionString("YuktiWorker")
    ?? builder.Configuration.GetConnectionString("Yukti")
    ?? throw new InvalidOperationException(
        "Missing ConnectionStrings:YuktiWorker/Yukti. Set one via `dotnet user-secrets set \"ConnectionStrings:Yukti\" \"...\"` for local development.");
builder.Services.AddYuktiInfrastructure(connectionString);

// EfUnitOfWorkFactory (registered above) flushes queued domain events
// through this dispatcher on every Commit — same in-process, Tier 1
// dispatcher Yukti.Api uses; nothing about it is HTTP-specific.
builder.Services.AddSingleton<InMemoryDomainEventDispatcher>();
builder.Services.AddSingleton<IDomainEventDispatcher>(sp => sp.GetRequiredService<InMemoryDomainEventDispatcher>());

// FR-SCHED-03: same dedicated "yukti-redis" instance Yukti.Api's trigger
// lock/SignalR backplane use — never cara-redis, never any other project's
// Redis. This process only needs the trigger lock, not SignalR.
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6380";
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));

// See AmbientTenantContextAccessor's doc comment: this process's
// tenant-filtered EF repositories need a settable source instead of
// Yukti.Api's HttpContext-claim-backed one.
builder.Services.AddScoped<ITenantContextAccessor, AmbientTenantContextAccessor>();
builder.Services.AddScoped<IPermissionChecker, PermissionChecker>();

// ITriggerRepository is now the real EfTriggerRepository, registered by
// AddYuktiInfrastructure above — previously InMemoryTriggerRepository here,
// which meant scheduled triggers vanished on every restart and were
// invisible to Yukti.Api (which never referenced the in-memory store at
// all). Scoped, not Singleton, matching every other Ef* repository.
builder.Services.AddSingleton<ITriggerLock, RedisTriggerLock>();
builder.Services.AddScoped<TriggerFlowRunCommandHandler>();
builder.Services.AddHostedService<SchedulerHostedService>();

builder.Services.AddScoped<ITier2EventConsumer<FlowRunCompletedEvent>, FlowReportProjectionConsumer>();
builder.Services.AddHostedService<OutboxRelayHostedService>();
builder.Services.AddHostedService<TrendAggregateBatchJob>();
builder.Services.AddHostedService<TablePartitioningMonitorHostedService>();

var host = builder.Build();
host.Run();
