using Yukti.Application.Ai;
using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure.Ai;

/// <summary>
/// D5-T9/§R6: composes <see cref="IAiUsageQuery"/> (spent so far this week) + <see
/// cref="IAiPricingProvider"/> (prices the projected call) + <see cref="IAiBudgetOptionsProvider"/>
/// (cap/warn threshold) into the one check <see cref="Yukti.Infrastructure.Ai.AiPlanningEngine"/>
/// calls. "This week" is the calendar week (<see cref="AiWeek.StartOfCurrentWeekUtc"/>), matching
/// <c>plan/AI-BUDGET.md</c>'s own framing — not a rolling 7 days.
///
/// DISC-14 adds a second, independent check via <see cref="IAiRequestQuotaLedger"/>: a provider's
/// own request-count quota (e.g. a free-tier Gemini key's 20/day) is a different constraint from
/// dollar cost — a free-tier call is $0, so the cost check above would never catch it. Either
/// check failing refuses the call; <see cref="Yukti.Infrastructure.Ai.AiPlanningEngine"/> reads
/// <see cref="AiBudgetCheck.QuotaExceeded"/> to raise the right exception for which one it was.
/// </summary>
public sealed class AiBudgetGuard : IAiBudgetGuard
{
    private readonly IAiUsageQuery _usageQuery;
    private readonly IAiPricingProvider _pricing;
    private readonly IAiBudgetOptionsProvider _options;
    private readonly IAiRequestQuotaLedger _quotaLedger;

    public AiBudgetGuard(IAiUsageQuery usageQuery, IAiPricingProvider pricing, IAiBudgetOptionsProvider options, IAiRequestQuotaLedger quotaLedger)
    {
        _usageQuery = usageQuery;
        _pricing = pricing;
        _options = options;
        _quotaLedger = quotaLedger;
    }

    public async Task<AiBudgetCheck> CheckAsync(TenantId tenantId, string provider, int estimatedInputTokens, int maxOutputTokens, CancellationToken ct)
    {
        var since = AiWeek.StartOfCurrentWeekUtc(DateTimeOffset.UtcNow);
        var usage = await _usageQuery.GetUsageSince(tenantId, since, ct);
        var projected = _pricing.GetFor(provider).CostFor(estimatedInputTokens, maxOutputTokens);
        var cap = _options.WeeklyCapUsd;

        var costAllowed = usage.TotalCostUsd + projected <= cap;
        var nearCap = cap > 0 && (usage.TotalCostUsd + projected) / cap >= (decimal)_options.WarnThreshold;

        var dailyRequestQuota = _options.DailyRequestQuota;
        var requestsToday = await _quotaLedger.CountTodayAsync(ct);
        var quotaExceeded = requestsToday >= dailyRequestQuota;

        return new AiBudgetCheck(
            Allowed: costAllowed && !quotaExceeded, nearCap, usage.TotalCostUsd, projected, cap,
            quotaExceeded, requestsToday, dailyRequestQuota);
    }
}
