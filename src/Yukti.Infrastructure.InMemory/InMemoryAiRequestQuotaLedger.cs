using System.Collections.Concurrent;
using Yukti.Application.Ai;

namespace Yukti.Infrastructure.InMemory;

/// <summary>DISC-14: the in-memory twin of <see cref="Yukti.Infrastructure.Ai.FileAiRequestQuotaLedger"/> —
/// tests (and the eval harness's fixture mode) never touch the real, shared on-disk ledger file.</summary>
public sealed class InMemoryAiRequestQuotaLedger : IAiRequestQuotaLedger
{
    private readonly ConcurrentBag<DateTimeOffset> _requestsUtc = new();

    public Task<int> CountTodayAsync(CancellationToken ct)
    {
        var today = DateTimeOffset.UtcNow.UtcDateTime.Date;
        return Task.FromResult(_requestsUtc.Count(t => t.UtcDateTime.Date == today));
    }

    public Task RecordAsync(string source, string capability, CancellationToken ct)
    {
        _requestsUtc.Add(DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }
}
