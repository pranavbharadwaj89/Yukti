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
    private readonly List<IHasDomainEvents> _trackedAggregates;
    private readonly Action<IReadOnlyList<IDomainEvent>> _dispatchEvents;

    internal InMemoryUnitOfWork(
        List<Action> pendingSaves,
        List<IHasDomainEvents> trackedAggregates,
        Action<IReadOnlyList<IDomainEvent>> dispatchEvents)
    {
        _pendingSaves = pendingSaves;
        _trackedAggregates = trackedAggregates;
        _dispatchEvents = dispatchEvents;
    }

    public Task Commit(CancellationToken ct)
    {
        foreach (var save in _pendingSaves)
            save();

        var events = _trackedAggregates.SelectMany(a => a.DomainEvents).ToList();
        foreach (var aggregate in _trackedAggregates)
            aggregate.ClearDomainEvents();
        _dispatchEvents(events);

        return Task.CompletedTask;
    }

    // FR-OPS-03 fallout: see IUnitOfWork.DiscardStaged's doc comment.
    // Clears both this instance's own snapshot lists — pending saves AND
    // the aggregates snapshot itself, not just the events already raised
    // on them — so a Commit() called afterward (to flush just a
    // failure-path audit entry) neither replays an abandoned mutation nor
    // dispatches a domain event an abandoned operation happened to have
    // already raised before it failed.
    public void DiscardStaged()
    {
        _pendingSaves.Clear();
        foreach (var aggregate in _trackedAggregates)
            aggregate.ClearDomainEvents();
        _trackedAggregates.Clear();
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
            aggregatesSnapshot,
            dispatchEvents: events => _dispatcher.DispatchAll(events));
    }
}
