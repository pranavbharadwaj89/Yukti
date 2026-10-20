using Xunit;
using Yukti.Domain.Ai;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;

namespace Yukti.Infrastructure.Tests;

/// <summary>
/// D5-T9/§R10: the aggregation both <see cref="Yukti.Infrastructure.Ai.AiBudgetGuard"/>'s pre-call
/// check and <c>GET /api/v1/ai/usage</c> depend on — tenant-scoped, since-scoped, and a budget
/// refusal (zero-cost, <c>Attempt: 0</c>) must count toward <see
/// cref="AiUsageSummary.BudgetRefusals"/> without polluting <c>Calls</c>/<c>Retries</c> for any
/// capability, since it never reached the provider.
/// </summary>
public sealed class InMemoryAiUsageQueryTests
{
    private static readonly DateTimeOffset Since = new(2026, 10, 13, 0, 0, 0, TimeSpan.Zero); // a Monday

    [Fact]
    public async Task Only_this_tenants_rows_since_the_given_time_are_summed()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var repository = new InMemoryAiCostRepository();
        await repository.Append(AiCostRecord.ForAttempt(tenantA, "coder", "gemini", "v1", 1, true, 1000, 500, 0.01m), default);
        await repository.Append(AiCostRecord.ForAttempt(tenantB, "coder", "gemini", "v1", 1, true, 9000, 9000, 9.00m), default); // other tenant
        var query = new InMemoryAiUsageQuery(repository);

        var summary = await query.GetUsageSince(tenantA, Since, default);

        Assert.Equal(0.01m, summary.TotalCostUsd);
        Assert.Equal(1000, summary.TotalInputTokens);
    }

    [Fact]
    public async Task Rows_before_the_since_boundary_are_excluded()
    {
        var tenant = TenantId.New();
        var repository = new InMemoryAiCostRepository();
        var oldRecord = AiCostRecord.ForAttempt(tenant, "coder", "gemini", "v1", 1, true, 1000, 1000, 1.00m);
        await repository.Append(oldRecord, default);
        var query = new InMemoryAiUsageQuery(repository);

        var summary = await query.GetUsageSince(tenant, Since.AddYears(10), default); // boundary is after every real record

        Assert.Equal(0m, summary.TotalCostUsd);
    }

    [Fact]
    public async Task A_budget_refusal_counts_toward_BudgetRefusals_but_not_Calls_or_cost()
    {
        var tenant = TenantId.New();
        var repository = new InMemoryAiCostRepository();
        await repository.Append(AiCostRecord.ForBudgetRefusal(tenant, "coder", "gemini"), default);
        var query = new InMemoryAiUsageQuery(repository);

        var summary = await query.GetUsageSince(tenant, Since, default);

        Assert.Equal(1, summary.BudgetRefusals);
        Assert.Equal(0m, summary.TotalCostUsd);
        var coder = Assert.Single(summary.ByCapability);
        Assert.Equal(0, coder.Calls); // the refusal never reached the provider, so it's not a "call"
    }

    [Fact]
    public async Task A_retry_counts_once_as_a_call_and_once_as_a_retry_per_capability()
    {
        // Attempt 1 (failed) + attempt 2 (succeeded) for the same logical generation: two rows,
        // but the usage view's own "calls vs retries" distinction (§R10) should show 2 calls, 1 of
        // which was a retry — not 2 independent generations.
        var tenant = TenantId.New();
        var repository = new InMemoryAiCostRepository();
        await repository.Append(AiCostRecord.ForAttempt(tenant, "coder", "gemini", "v1", 1, false, 10, 5, 0.001m), default);
        await repository.Append(AiCostRecord.ForAttempt(tenant, "coder", "gemini", "v1", 2, true, 12, 8, 0.002m), default);
        var query = new InMemoryAiUsageQuery(repository);

        var summary = await query.GetUsageSince(tenant, Since, default);

        var coder = Assert.Single(summary.ByCapability);
        Assert.Equal("coder", coder.Capability);
        Assert.Equal(2, coder.Calls);
        Assert.Equal(1, coder.Retries);
    }
}
