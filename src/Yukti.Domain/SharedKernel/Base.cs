namespace Yukti.Domain.SharedKernel;

/// <summary>
/// Raised when a domain invariant is violated. Reserved for truly invalid
/// states a well-behaved caller should never trigger — most domain rejections
/// are typed results (e.g. FlowPublishResult), not exceptions.
/// Per Volume 5 §7.4's exception hierarchy, this derives conceptually from
/// the same root every Yukti exception does; kept standalone here since
/// Yukti.Domain has zero outward dependencies (Volume 1 Part III §15.5).
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}

public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}

public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}

/// <summary>
/// Base for identity-bearing objects with a lifecycle independent of their
/// current attribute values (Volume 1 Part II §10.1).
/// </summary>
public abstract class Entity<TId> where TId : struct
{
    public TId Id { get; protected set; }

    protected Entity(TId id) => Id = id;

    public override bool Equals(object? obj) =>
        obj is Entity<TId> other && EqualityComparer<TId>.Default.Equals(Id, other.Id);

    public override int GetHashCode() => Id.GetHashCode();
}

/// <summary>
/// Base for aggregate roots. Owns domain-event buffering — a root records
/// events as its behavior methods run; the Unit of Work (application layer)
/// collects and dispatches them only once the aggregate's changes are
/// successfully persisted (Volume 1 Part III §16.3-16.4's outbox pattern).
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents where TId : struct
{
    private readonly List<IDomainEvent> _domainEvents = new();
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected AggregateRoot(TId id) : base(id) { }

    protected void RaiseDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
