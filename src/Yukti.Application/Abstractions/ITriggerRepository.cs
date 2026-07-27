using Yukti.Domain.Scheduling;
using Yukti.Domain.SharedKernel;

namespace Yukti.Application.Abstractions;

public interface ITriggerRepository
{
    Task<TriggerDefinition?> GetById(TriggerId id, CancellationToken ct);
    Task<TriggerDefinition?> GetByWebhookPath(string webhookPath, CancellationToken ct);
    Task<IReadOnlyList<TriggerDefinition>> GetEnabledCronTriggers(CancellationToken ct);
    Task Save(TriggerDefinition trigger, CancellationToken ct);
}

/// <summary>
/// FR-SCHED-03: a distributed lock keyed per (TriggerId, tick-window) so
/// N horizontally-scaled scheduler instances firing the same cron tick at
/// the same moment produce exactly one TriggerFlowRunCommand, not N.
/// InMemoryTriggerLock (Yukti.Infrastructure.InMemory) is a real,
/// correct single-process implementation — genuinely cross-instance
/// locking needs a shared backend (Redis SETNX, a DB unique-constraint
/// row, etc.) this environment doesn't have deployed, the same documented
/// category of gap as the Redis-backed rate limiter elsewhere in this repo.
/// </summary>
public interface ITriggerLock
{
    /// <summary>Returns true if the caller acquired the lock for this
    /// (triggerId, tickWindow) and should proceed to fire; false if
    /// another caller already holds/held it.</summary>
    Task<bool> TryAcquire(TriggerId triggerId, DateTimeOffset tickWindow, CancellationToken ct);
}
