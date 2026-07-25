using Yukti.Application.Abstractions;
using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure.InMemory;

/// <summary>
/// In-memory stand-in for the real outbox-backed Unit of Work (Volume 1
/// Part III §16.3-16.4). Real durability (the entire point of §16.7's
/// per-step commit design) requires a real database — this implementation
/// proves the CALL PATTERN is correct (one commit per step, dispatching
/// events each time) without yet proving real crash-durability, which only
/// a real database-backed implementation can provide.
/// </summary>
public sealed class InMemoryUnitOfWork : IUnitOfWork
{
    private readonly List<Action> _pendingSaves;
    private readonly Action<IReadOnlyList<IDomainEvent>> _dispatchEvents;
    private readonly Func<IReadOnlyList<IDomainEvent>> _collectEvents;

    internal InMemoryUnitOfWork(
        List<Action> pendingSaves,
        Func<IReadOnlyList<IDomainEvent>> collectEvents,
        Action<IReadOnlyList<IDomainEvent>> dispatchEvents)
    {
        _pendingSaves = pendingSaves;
        _collectEvents = collectEvents;
        _dispatchEvents = dispatchEvents;
    }

    public Task Commit(CancellationToken ct)
    {
        foreach (var save in _pendingSaves)
            save();

        var events = _collectEvents();
        _dispatchEvents(events);

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class InMemoryUnitOfWorkFactory : IUnitOfWorkFactory
{
    private readonly List<Action> _pendingSaves = new();
    private readonly List<IHasDomainEvents> _trackedAggregates = new();
    private readonly IDomainEventDispatcher _dispatcher;

    public InMemoryUnitOfWorkFactory(IDomainEventDispatcher dispatcher) => _dispatcher = dispatcher;

    /// <summary>Repositories call this to stage a save and register the aggregate for event collection.</summary>
    public void StageSave(Action save, IHasDomainEvents? aggregate = null)
    {
        _pendingSaves.Add(save);
        if (aggregate is not null)
            _trackedAggregates.Add(aggregate);
    }

    public IUnitOfWork Create()
    {
        var savesSnapshot = new List<Action>(_pendingSaves);
        var aggregatesSnapshot = new List<IHasDomainEvents>(_trackedAggregates);
        _pendingSaves.Clear();
        _trackedAggregates.Clear();

        return new InMemoryUnitOfWork(
            savesSnapshot,
            collectEvents: () =>
            {
                var events = aggregatesSnapshot.SelectMany(a => a.DomainEvents).ToList();
                foreach (var aggregate in aggregatesSnapshot)
                    aggregate.ClearDomainEvents();
                return events;
            },
            dispatchEvents: events => _dispatcher.DispatchAll(events));
    }
}
