using Microsoft.EntityFrameworkCore;
using Yukti.Application.Abstractions;
using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure;

/// <summary>
/// The real, durable counterpart to InMemoryUnitOfWork — Commit() is a
/// genuine CockroachDB transaction via DbContext.SaveChangesAsync, not a
/// call-pattern rehearsal. Domain events raised on any tracked aggregate
/// are collected and dispatched only after that transaction succeeds
/// (Volume 1 Part III §16.3-16.4's outbox pattern's Tier-1 half — see
/// IDomainEventDispatcher's own doc comment for what's still Tier-2-only).
///
/// FlowEngine calls IUnitOfWorkFactory.Create() once per step and commits
/// each independently (§16.7/§19.2) — this factory doesn't spin up a new
/// DbContext per call; it wraps the single scoped DbContext for the
/// request, so each Commit() is simply another SaveChangesAsync flush,
/// which CockroachDB durably persists as its own transaction the moment it
/// returns.
/// </summary>
public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly YuktiDbContext _context;
    private readonly IDomainEventDispatcher _dispatcher;

    public EfUnitOfWork(YuktiDbContext context, IDomainEventDispatcher dispatcher)
    {
        _context = context;
        _dispatcher = dispatcher;
    }

    public async Task Commit(CancellationToken ct)
    {
        var trackedAggregates = _context.ChangeTracker.Entries()
            .Select(e => e.Entity)
            .OfType<IHasDomainEvents>()
            .Where(a => a.DomainEvents.Count > 0)
            .ToList();

        var events = trackedAggregates.SelectMany(a => a.DomainEvents).ToList();

        // FR-EVT-01's Tier 2 write: one outbox row per event, added to the
        // SAME change-tracked SaveChangesAsync call as the aggregate state
        // below — this is what makes "state committed, event never
        // durably recorded" unreachable, not two separate calls that
        // could fail independently.
        foreach (var domainEvent in events)
            _context.Add(OutboxMessage.From(domainEvent));

        await _context.SaveChangesAsync(ct);

        foreach (var aggregate in trackedAggregates)
            aggregate.ClearDomainEvents();

        // Tier 1: synchronous in-process dispatch, unchanged from before —
        // SignalR live-progress subscribes only this (FR-EVT-03).
        if (events.Count > 0)
            _dispatcher.DispatchAll(events);
    }

    public void DiscardStaged() => _context.ChangeTracker.Clear();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class EfUnitOfWorkFactory : IUnitOfWorkFactory
{
    private readonly YuktiDbContext _context;
    private readonly IDomainEventDispatcher _dispatcher;

    public EfUnitOfWorkFactory(YuktiDbContext context, IDomainEventDispatcher dispatcher)
    {
        _context = context;
        _dispatcher = dispatcher;
    }

    public IUnitOfWork Create() => new EfUnitOfWork(_context, _dispatcher);
}
