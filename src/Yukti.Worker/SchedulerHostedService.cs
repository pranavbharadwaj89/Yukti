using Yukti.Application.Abstractions;
using Yukti.Application.Execution;
using Yukti.Domain.Execution;
using Yukti.Domain.Scheduling;

namespace Yukti.Worker;

/// <summary>
/// FR-SCHED: polls enabled Cron triggers, computes missed ticks within a
/// bounded catch-up window (FR-SCHED-05), and fires each via a
/// distributed-lock-guarded TriggerFlowRunCommand (FR-SCHED-02/03) — the
/// exact same command an API-triggered run issues, differing only in
/// RunTrigger.Scheduled. Each tick runs in its own DI scope, and
/// AmbientTenantContextAccessor.CurrentTenantId is set to that trigger's
/// tenant before the scope's handler runs: TriggerFlowRunCommandHandler and
/// the PermissionChecker it calls both resolve tenant-filtered EF
/// repositories, and one poll spans every tenant's triggers, not just one —
/// a single scope shared across the whole poll (the original, pre-split
/// shape of this method) would leave every repository query after the
/// first trigger filtering on the wrong tenant.
/// </summary>
public sealed class SchedulerHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CatchUpWindow = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SchedulerHostedService> _logger;

    public SchedulerHostedService(IServiceScopeFactory scopeFactory, ILogger<SchedulerHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                await PollOnce(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Scheduler poll failed: {Error}", ex.Message);
            }
        } while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PollOnce(CancellationToken ct)
    {
        // Enumerating triggers needs no tenant context of its own —
        // InMemoryTriggerRepository isn't tenant-filtered (see its own doc
        // comment: durable, tenant-scoped persistence is a follow-up) — so
        // a short-lived scope here is only to resolve it, not to carry
        // state into the per-tick work below.
        IReadOnlyList<TriggerDefinition> cronTriggers;
        using (var listScope = _scopeFactory.CreateScope())
        {
            var triggerRepository = listScope.ServiceProvider.GetRequiredService<ITriggerRepository>();
            cronTriggers = await triggerRepository.GetEnabledCronTriggers(ct);
        }

        var now = DateTimeOffset.UtcNow;

        foreach (var trigger in cronTriggers)
        {
            var missedTicks = CronExpressionEvaluator.GetMissedTicks(
                trigger.CronExpression!, trigger.LastFiredAt, now, CatchUpWindow);

            foreach (var tick in missedTicks)
            {
                using var scope = _scopeFactory.CreateScope();
                var tenantAccessor = (AmbientTenantContextAccessor)scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
                tenantAccessor.CurrentTenantId = trigger.TenantId;

                var triggerLock = scope.ServiceProvider.GetRequiredService<ITriggerLock>();
                if (!await triggerLock.TryAcquire(trigger.Id, tick, ct))
                {
                    _logger.LogDebug("Trigger {TriggerId} tick {Tick} already claimed by another instance", trigger.Id.Value, tick);
                    continue;
                }

                _logger.LogInformation("Firing cron trigger {TriggerId} for tick {Tick}", trigger.Id.Value, tick);
                var triggerHandler = scope.ServiceProvider.GetRequiredService<TriggerFlowRunCommandHandler>();
                await triggerHandler.Handle(
                    new TriggerFlowRunCommand(trigger.FlowId, RunTrigger.Scheduled, null, trigger.TenantId, trigger.RegisteredBy), ct);

                trigger.RecordFired(tick);
                var triggerRepository = scope.ServiceProvider.GetRequiredService<ITriggerRepository>();
                await triggerRepository.Save(trigger, ct);
                var uowFactory = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();
                await using var uow = uowFactory.Create();
                await uow.Commit(ct);
            }
        }
    }
}
