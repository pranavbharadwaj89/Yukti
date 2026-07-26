using Yukti.Domain.SharedKernel;

namespace Yukti.Application.Abstractions;

/// <summary>
/// Tier 1 (synchronous in-process dispatch) of the two-tier Event Bus
/// design (Volume 1 Part III §22.2). Tier 2 (durable, outbox-backed,
/// at-least-once relay) is a separate, not-yet-built concern — this
/// interface only covers the in-process pub/sub every IUnitOfWork.Commit()
/// call feeds after a successful persist, regardless of which
/// Infrastructure implementation (in-memory or EF Core/CockroachDB) did
/// the persisting.
/// </summary>
public interface IDomainEventDispatcher
{
    void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IDomainEvent;
    void DispatchAll(IReadOnlyList<IDomainEvent> events);
}
