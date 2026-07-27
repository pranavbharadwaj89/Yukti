using Yukti.Domain.SharedKernel;

namespace Yukti.Application.Abstractions;

/// <summary>
/// FR-EVT-03: a consumer that subscribes Tier 2 (the durable outbox
/// relay), as opposed to Tier 1 (IDomainEventDispatcher's synchronous
/// in-process pub/sub — SignalR live-progress subscribes only that one).
/// FR-EVT-02: every implementation MUST be idempotent — the outbox relay
/// is at-least-once, so the same event can be redelivered (e.g. after a
/// process crash between "dispatched" and "marked processed"). Upsert by
/// a stable key derived from the event; never a blind insert.
/// </summary>
public interface ITier2EventConsumer<in TEvent> where TEvent : IDomainEvent
{
    Task Handle(TEvent domainEvent, CancellationToken ct);
}
