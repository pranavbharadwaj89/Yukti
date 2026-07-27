using System.Collections.Concurrent;
using Yukti.Application.Abstractions;
using Yukti.Domain.Scheduling;
using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure.InMemory;

/// <summary>
/// Singleton, process-lifetime store — unlike every other InMemory
/// repository in this project, it does NOT route Save through
/// InMemoryUnitOfWorkFactory's staged-commit/domain-event-dispatch
/// pipeline, because TriggerDefinition never raises a domain event
/// (it has no RaiseDomainEvent call anywhere) — there is nothing for a
/// UnitOfWork to flush. This also sidesteps a real wiring gap: Yukti.Api
/// composes exclusively against EfUnitOfWorkFactory, so no
/// InMemoryUnitOfWorkFactory instance exists there to inject even if this
/// repository wanted one. Durable (EF-backed) trigger persistence is a
/// mechanical follow-up matching every other aggregate's EF configuration,
/// not implemented this pass — same documented-gap category as this
/// project's other "real thing not built yet" notes.
/// </summary>
public sealed class InMemoryTriggerRepository : ITriggerRepository
{
    private readonly ConcurrentDictionary<Guid, TriggerDefinition> _store = new();

    public Task<TriggerDefinition?> GetById(TriggerId id, CancellationToken ct) =>
        Task.FromResult(_store.TryGetValue(id.Value, out var trigger) ? trigger : null);

    public Task<TriggerDefinition?> GetByWebhookPath(string webhookPath, CancellationToken ct) =>
        Task.FromResult(_store.Values.FirstOrDefault(t => t.WebhookPath == webhookPath));

    public Task<IReadOnlyList<TriggerDefinition>> GetEnabledCronTriggers(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<TriggerDefinition>>(
            _store.Values.Where(t => t.Kind == TriggerKind.Cron && t.IsEnabled).ToList());

    public Task Save(TriggerDefinition trigger, CancellationToken ct)
    {
        _store[trigger.Id.Value] = trigger;
        return Task.CompletedTask;
    }
}

/// <summary>Single-process stand-in for a Redis-backed lock — see
/// ITriggerLock's doc comment for the real cross-instance gap.</summary>
public sealed class InMemoryTriggerLock : ITriggerLock
{
    private readonly ConcurrentDictionary<(Guid TriggerId, DateTimeOffset TickWindow), bool> _held = new();

    public Task<bool> TryAcquire(TriggerId triggerId, DateTimeOffset tickWindow, CancellationToken ct) =>
        Task.FromResult(_held.TryAdd((triggerId.Value, tickWindow), true));
}
