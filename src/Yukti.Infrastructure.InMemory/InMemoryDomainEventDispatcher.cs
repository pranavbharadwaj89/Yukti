using Yukti.Application.Abstractions;
using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure.InMemory;

/// <summary>
/// Tier 1 in-process dispatcher — IDomainEventDispatcher itself lives in
/// Yukti.Application.Abstractions now (shared port, so the real EF Core
/// Infrastructure project can depend on the interface without depending on
/// this demo project). No durable Tier 2 relay yet; that's a follow-up once
/// an audit pipeline/report projector actually needs at-least-once delivery.
/// </summary>
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
