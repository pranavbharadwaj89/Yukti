using System.Collections.Concurrent;
using Yukti.Application.Abstractions;
using Yukti.Domain.Auditing;

namespace Yukti.Infrastructure.InMemory;

/// <summary>
/// Demo-grade stand-in for EfAuditRepository (Yukti.Infrastructure),
/// matching the InMemoryCredentialResolver/InMemoryIdempotencyStore
/// pattern elsewhere in this project — temporary, replaced with zero
/// change to any command handler since all of them depend only on
/// IAuditRepository. Exposes Entries for tests/smoke-run inspection only;
/// this is not part of IAuditRepository's contract.
/// </summary>
public sealed class InMemoryAuditRepository : IAuditRepository
{
    private readonly ConcurrentQueue<AuditEntry> _entries = new();

    public IReadOnlyCollection<AuditEntry> Entries => _entries.ToArray();

    public Task Append(AuditEntry entry, CancellationToken ct)
    {
        _entries.Enqueue(entry);
        return Task.CompletedTask;
    }
}
