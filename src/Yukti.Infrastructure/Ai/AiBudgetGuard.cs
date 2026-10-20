using Yukti.Application.Ai;
using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure.Ai;

/// <summary>
/// D5-T9/§R6: composes <see cref="IAiUsageQuery"/> (spent so far this week) + <see
/// cref="IAiPricingProvider"/> (prices the projected call) + <see cref="IAiBudgetOptionsProvider"/>
/// (cap/warn threshold) into the one check <see cref="Yukti.Infrastructure.Ai.AiPlanningEngine"/>
/// calls. "This week" is the calendar week (<see cref="AiWeek.StartOfCurrentWeekUtc"/>), matching
/// <c>plan/AI-BUDGET.md</c>'s own framing — not a rolling 7 days.
/// </summary>
public sealed class AiBudgetGuard : IAiBudgetGuard
{
    private readonly IAiUsageQuery _usageQuery;
    private readonly IAiPricingProvider _pricing;
    private readonly IAiBudgetOptionsProvider _options;

    public AiBudgetGuard(IAiUsageQuery usageQuery, IAiPricingProvider pricing, IAiBudgetOptionsProvider options)
    {
        _usageQuery = usageQuery;
        _pricing = pricing;
        _options = options;
    }

    public async Task<AiBudgetCheck> CheckAsync(TenantId tenantId, string provider, int estimatedInputTokens, int maxOutputTokens, CancellationToken ct)
    {
        var since = AiWeek.StartOfCurrentWeekUtc(DateTimeOffset.UtcNow);
        var usage = await _usageQuery.GetUsageSince(tenantId, since, ct);
        var projected = _pricing.GetFor(provider).CostFor(estimatedInputTokens, maxOutputTokens);
        var cap = _options.WeeklyCapUsd;

        var allowed = usage.TotalCostUsd + projected <= cap;
        var nearCap = cap > 0 && (usage.TotalCostUsd + projected) / cap >= (decimal)_options.WarnThreshold;

        return new AiBudgetCheck(allowed, nearCap, usage.TotalCostUsd, projected, cap);
    }
}
