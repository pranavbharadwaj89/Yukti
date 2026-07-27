using System.Text.Json;
using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure;

/// <summary>
/// FR-EVT-01's Tier 2 durable relay, backed by the exact same
/// commit-time write EfUnitOfWork already performs for Tier 1 (FR-REPO-05)
/// — one row per domain event, inserted in the SAME SaveChangesAsync call
/// that persists the aggregate's state, so "state committed but the event
/// never durably recorded" is not a reachable failure mode: either both
/// happen or neither does. OutboxRelayHostedService is what actually
/// delivers these to Tier 2 consumers, independently and after the fact.
/// </summary>
public sealed class OutboxMessage
{
    public required OutboxMessageId Id { get; init; }
    public required string EventTypeName { get; init; } // CLR AssemblyQualifiedName, so the relay can Type.GetType() it back
    public required string PayloadJson { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; set; }

    public static OutboxMessage From(IDomainEvent domainEvent)
    {
        var type = domainEvent.GetType();
        return new OutboxMessage
        {
            Id = OutboxMessageId.New(),
            EventTypeName = type.AssemblyQualifiedName!,
            PayloadJson = JsonSerializer.Serialize(domainEvent, type),
            OccurredAt = domainEvent.OccurredAt,
        };
    }

    public IDomainEvent Deserialize()
    {
        var type = Type.GetType(EventTypeName)
            ?? throw new InvalidOperationException($"Cannot resolve outbox event type '{EventTypeName}'.");
        return (IDomainEvent)JsonSerializer.Deserialize(PayloadJson, type)!;
    }
}
