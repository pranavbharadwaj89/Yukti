using Microsoft.EntityFrameworkCore;
using Yukti.Application.Abstractions;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure;

namespace Yukti.Worker;

/// <summary>
/// FR-EVT-01's Tier 2 relay: polls outbox_messages for unprocessed rows,
/// dispatches each to every registered ITier2EventConsumer&lt;TEvent&gt; for
/// that event's concrete type, then marks it processed. At-least-once by
/// construction — a crash between "consumer ran" and "marked processed"
/// redelivers the same row next poll, which is exactly why FR-EVT-02
/// requires every consumer to be idempotent rather than relying on this
/// relay to guarantee exactly-once (it deliberately doesn't try to).
/// </summary>
public sealed class OutboxRelayHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxRelayHostedService> _logger;

    public OutboxRelayHostedService(IServiceScopeFactory scopeFactory, ILogger<OutboxRelayHostedService> logger)
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
                await RelayOnce(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Outbox relay poll failed: {Error}", ex.Message);
            }
        } while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RelayOnce(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<YuktiDbContext>();

        var pending = await context.OutboxMessages
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.OccurredAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var message in pending)
        {
            IDomainEvent domainEvent;
            try
            {
                domainEvent = message.Deserialize();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox message {MessageId} failed to deserialize as {EventType} — skipping",
                    message.Id.Value, message.EventTypeName);
                continue;
            }

            await DispatchToConsumers(scope.ServiceProvider, domainEvent, ct);

            message.ProcessedAt = DateTimeOffset.UtcNow;
        }

        if (pending.Count > 0)
            await context.SaveChangesAsync(ct);
    }

    private async Task DispatchToConsumers(IServiceProvider services, IDomainEvent domainEvent, CancellationToken ct)
    {
        var consumerInterface = typeof(ITier2EventConsumer<>).MakeGenericType(domainEvent.GetType());
        var consumers = services.GetServices(consumerInterface);

        foreach (var consumer in consumers)
        {
            var handleMethod = consumerInterface.GetMethod("Handle")!;
            await (Task)handleMethod.Invoke(consumer, new object[] { domainEvent, ct })!;
        }
    }
}
