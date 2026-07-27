using StackExchange.Redis;
using Yukti.Application.Abstractions;
using Yukti.Domain.SharedKernel;

namespace Yukti.Worker;

/// <summary>
/// FR-SCHED-03: the real cross-instance implementation InMemoryTriggerLock's
/// doc comment describes as the closing move — SET key value NX (Redis's
/// atomic "set if not exists") is exactly the same primitive as the
/// in-memory ConcurrentDictionary.TryAdd it replaces, just visible to every
/// horizontally-scaled Yukti.Worker instance instead of one process. Keys
/// carry a TTL longer than the scheduler's catch-up window so stale locks
/// don't accumulate forever. Runs against a Redis instance dedicated to
/// this project (the "yukti-redis" Docker container); the
/// "yukti:trigger-lock:" prefix is retained regardless as good hygiene,
/// not because this Redis is shared with anything else.
/// </summary>
public sealed class RedisTriggerLock : ITriggerLock
{
    private static readonly TimeSpan LockTtl = TimeSpan.FromHours(1);
    private readonly IConnectionMultiplexer _redis;

    public RedisTriggerLock(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<bool> TryAcquire(TriggerId triggerId, DateTimeOffset tickWindow, CancellationToken ct)
    {
        var key = $"yukti:trigger-lock:{triggerId.Value}:{tickWindow:O}";
        var db = _redis.GetDatabase();
        return await db.StringSetAsync(key, value: "1", expiry: LockTtl, when: When.NotExists);
    }
}
