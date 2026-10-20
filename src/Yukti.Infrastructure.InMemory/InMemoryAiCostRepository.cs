using System.Collections.Concurrent;
using Yukti.Application.Abstractions;
using Yukti.Domain.Ai;

namespace Yukti.Infrastructure.InMemory;

/// <summary>D5-T9: same pattern as <see cref="InMemoryAuditRepository"/> — an append-only in-process queue.</summary>
public sealed class InMemoryAiCostRepository : IAiCostRepository
{
    private readonly ConcurrentQueue<AiCostRecord> _records = new();

    public IReadOnlyCollection<AiCostRecord> Records => _records.ToArray();

    public Task Append(AiCostRecord record, CancellationToken ct)
    {
        _records.Enqueue(record);
        return Task.CompletedTask;
    }
}
