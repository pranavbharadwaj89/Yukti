using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure.InMemory;

/// <summary>
/// Demo-grade stand-in for the real two-tier Event Bus (Volume 1 Part III
/// §22.2's in-process-plus-outbox design) — this is Tier 1 only (synchronous
/// in-process dispatch), with no durable relay. Real event consumers
/// (audit pipeline, report projector) are a follow-up once Infrastructure
/// is backed by a real database.
/// </summary>
public interface IDomainEventDispatcher
{
    void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IDomainEvent;
    void DispatchAll(IReadOnlyList<IDomainEvent> events);
}

public sealed class InMemoryDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly Dictionary<Type, List<Action<IDomainEvent>>> _handlers = new();

    public void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IDomainEvent
    {
        var type = typeof(TEvent);
        if (!_handlers.TryGetValue(type, out var list))
            _handlers[type] = list = new List<Action<IDomainEvent>>();
        list.Add(e => handler((TEvent)e));
    }

    public void DispatchAll(IReadOnlyList<IDomainEvent> events)
    {
        foreach (var evt in events)
        {
            if (_handlers.TryGetValue(evt.GetType(), out var list))
                foreach (var handler in list)
                    handler(evt);
        }
    }
}
