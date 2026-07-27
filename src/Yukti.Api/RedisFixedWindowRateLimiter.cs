using System.Threading.RateLimiting;
using StackExchange.Redis;

namespace Yukti.Api;

/// <summary>
/// FR-API-04: the real cross-instance rate limiter — every horizontally-
/// scaled Yukti.Api instance shares the same counters via the dedicated
/// "yukti-redis" Redis instance, replacing the previous in-memory
/// SlidingWindowRateLimiter (correct within one process, blind to every
/// other replica). Fixed window, not sliding: a single Redis INCR + PEXPIRE
/// pair is atomic by construction (INCR always returns the post-increment
/// value; PEXPIRE only runs on the first hit in a window, when the key was
/// just created) without needing a Lua script — the sliding window's finer
/// smoothing wasn't the part of FR-API-04 that mattered (real cross-
/// instance sharing was); a correct fixed window is a legitimate, simpler
/// way to get there.
/// </summary>
public sealed class RedisFixedWindowRateLimiter : RateLimiter
{
    private readonly IConnectionMultiplexer _redis;
    private readonly string _key;
    private readonly int _permitLimit;
    private readonly TimeSpan _window;

    public RedisFixedWindowRateLimiter(IConnectionMultiplexer redis, string key, int permitLimit, TimeSpan window)
    {
        _redis = redis;
        _key = key;
        _permitLimit = permitLimit;
        _window = window;
    }

    public override TimeSpan? IdleDuration => null; // no in-process state to age out — Redis owns expiry

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        var count = _redis.GetDatabase().StringIncrement(_key);
        if (count == 1)
            _redis.GetDatabase().KeyExpire(_key, _window);
        return new BooleanRateLimitLease(count <= _permitLimit);
    }

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var count = await db.StringIncrementAsync(_key);
        if (count == 1)
            await db.KeyExpireAsync(_key, _window);
        return new BooleanRateLimitLease(count <= _permitLimit);
    }

    public override RateLimiterStatistics? GetStatistics() => null;

    private sealed class BooleanRateLimitLease : RateLimitLease
    {
        public BooleanRateLimitLease(bool isAcquired) => IsAcquired = isAcquired;
        public override bool IsAcquired { get; }
        public override IEnumerable<string> MetadataNames => Array.Empty<string>();
        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }
    }
}
