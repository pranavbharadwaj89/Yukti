using Yukti.Application.Abstractions;
using Yukti.Application.Execution;
using Yukti.Domain.Execution;
using Yukti.Domain.Scheduling;

namespace Yukti.Api;

/// <summary>
/// FR-SCHED: polls enabled Cron triggers, computes missed ticks within a
/// bounded catch-up window (FR-SCHED-05), and fires each via a
/// distributed-lock-guarded TriggerFlowRunCommand (FR-SCHED-02/03) — the
/// exact same command an API-triggered run issues, differing only in
/// RunTrigger.Scheduled. Each poll runs in its own DI scope since
/// TriggerFlowRunCommandHandler depends on Scoped EF repositories.
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
        using var scope = _scopeFactory.CreateScope();
        var triggerRepository = scope.ServiceProvider.GetRequiredService<ITriggerRepository>();
        var triggerLock = scope.ServiceProvider.GetRequiredService<ITriggerLock>();
        var triggerHandler = scope.ServiceProvider.GetRequiredService<TriggerFlowRunCommandHandler>();
        var uowFactory = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();

        var now = DateTimeOffset.UtcNow;
        var cronTriggers = await triggerRepository.GetEnabledCronTriggers(ct);

        foreach (var trigger in cronTriggers)
        {
            var missedTicks = CronExpressionEvaluator.GetMissedTicks(
                trigger.CronExpression!, trigger.LastFiredAt, now, CatchUpWindow);

            foreach (var tick in missedTicks)
            {
                if (!await triggerLock.TryAcquire(trigger.Id, tick, ct))
                {
                    _logger.LogDebug("Trigger {TriggerId} tick {Tick} already claimed by another instance", trigger.Id.Value, tick);
                    continue;
                }

                _logger.LogInformation("Firing cron trigger {TriggerId} for tick {Tick}", trigger.Id.Value, tick);
                await triggerHandler.Handle(
                    new TriggerFlowRunCommand(trigger.FlowId, RunTrigger.Scheduled, null, trigger.TenantId, trigger.RegisteredBy), ct);

                trigger.RecordFired(tick);
                await triggerRepository.Save(trigger, ct);
                await using var uow = uowFactory.Create();
                await uow.Commit(ct);
            }
        }
    }
}
