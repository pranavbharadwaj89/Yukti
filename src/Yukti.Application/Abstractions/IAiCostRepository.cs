using Yukti.Domain.Ai;

namespace Yukti.Application.Abstractions;

/// <summary>D5-T9: the write side of <c>ai_cost_records</c> — append-only, mirrors <see cref="IAuditRepository"/> exactly.</summary>
public interface IAiCostRepository
{
    Task Append(AiCostRecord record, CancellationToken ct);
}
