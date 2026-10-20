using Yukti.Application.Ai;
using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure.InMemory;

/// <summary>D5-T9: reads straight off the same <see cref="InMemoryAiCostRepository"/> every call records into — the in-memory twin of <c>EfAiUsageQuery</c>, same aggregation logic.</summary>
public sealed class InMemoryAiUsageQuery : IAiUsageQuery
{
    private readonly InMemoryAiCostRepository _repository;
    public InMemoryAiUsageQuery(InMemoryAiCostRepository repository) => _repository = repository;

    public Task<AiUsageSummary> GetUsageSince(TenantId tenantId, DateTimeOffset since, CancellationToken ct)
    {
        var rows = _repository.Records.Where(r => r.TenantId == tenantId && r.OccurredAt >= since).ToList();

        var byCapability = rows
            .GroupBy(r => r.Capability)
            .Select(g => new AiCapabilityUsage(
                g.Key,
                Calls: g.Count(r => !r.BudgetRefused),
                Retries: g.Count(r => !r.BudgetRefused && r.Attempt > 1),
                InputTokens: g.Sum(r => r.InputTokens),
                OutputTokens: g.Sum(r => r.OutputTokens),
                CostUsd: g.Sum(r => r.CostUsd)))
            .ToList();

        return Task.FromResult(new AiUsageSummary(
            TotalCostUsd: rows.Sum(r => r.CostUsd),
            TotalInputTokens: rows.Sum(r => r.InputTokens),
            TotalOutputTokens: rows.Sum(r => r.OutputTokens),
            BudgetRefusals: rows.Count(r => r.BudgetRefused),
            ByCapability: byCapability));
    }
}
