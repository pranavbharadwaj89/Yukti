using Xunit;
using Yukti.Application.Ai;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.Ai;

namespace Yukti.Infrastructure.Tests;

/// <summary>
/// D5-T9/§R6: the engine's own worst-case-before-calling check — composed from <see
/// cref="IAiUsageQuery"/> (spent so far this week) + <see cref="IAiPricingProvider"/> (prices the
/// projected call) + <see cref="IAiBudgetOptionsProvider"/> (cap/warn threshold), per <see
/// cref="AiBudgetGuard"/>'s own doc comment on why those three compose into this one port.
/// </summary>
public sealed class AiBudgetGuardTests
{
    private static readonly TenantId SomeTenant = TenantId.New();

    private sealed class FixedUsageQuery : IAiUsageQuery
    {
        private readonly decimal _spentUsd;
        public FixedUsageQuery(decimal spentUsd) => _spentUsd = spentUsd;
        public Task<AiUsageSummary> GetUsageSince(TenantId tenantId, DateTimeOffset since, CancellationToken ct) =>
            Task.FromResult(new AiUsageSummary(_spentUsd, 0, 0, 0, Array.Empty<AiCapabilityUsage>()));
    }

    private sealed class FixedPricingProvider : IAiPricingProvider
    {
        private readonly AiPricing _pricing;
        public FixedPricingProvider(AiPricing pricing) => _pricing = pricing;
        public AiPricing GetFor(string provider) => _pricing;
    }

    private sealed class FixedBudgetOptions : IAiBudgetOptionsProvider
    {
        public FixedBudgetOptions(decimal weeklyCapUsd, double warnThreshold, int dailyRequestQuota = int.MaxValue)
        {
            WeeklyCapUsd = weeklyCapUsd;
            WarnThreshold = warnThreshold;
            DailyRequestQuota = dailyRequestQuota;
        }
        public decimal WeeklyCapUsd { get; }
        public double WarnThreshold { get; }
        public int DailyRequestQuota { get; }
    }

    private sealed class FixedRequestQuotaLedger : IAiRequestQuotaLedger
    {
        private readonly int _requestsToday;
        public FixedRequestQuotaLedger(int requestsToday = 0) => _requestsToday = requestsToday;
        public Task<int> CountTodayAsync(CancellationToken ct) => Task.FromResult(_requestsToday);
        public Task RecordAsync(string source, string capability, CancellationToken ct) => Task.CompletedTask;
    }

    private static AiBudgetGuard BuildGuard(
        decimal spentUsd, AiPricing pricing, decimal capUsd, double warnThreshold = 0.8,
        int requestsToday = 0, int dailyRequestQuota = int.MaxValue) =>
        new(new FixedUsageQuery(spentUsd), new FixedPricingProvider(pricing),
            new FixedBudgetOptions(capUsd, warnThreshold, dailyRequestQuota), new FixedRequestQuotaLedger(requestsToday));

    [Fact]
    public async Task Well_under_the_cap_and_under_the_warn_threshold_is_allowed_and_not_near_cap()
    {
        // $5 spent + a cheap projected call, $30 cap, 80% warn — nowhere close to either line.
        var guard = BuildGuard(spentUsd: 5.00m, pricing: new AiPricing(InputPer1M: 1m, OutputPer1M: 1m), capUsd: 30.00m);

        var check = await guard.CheckAsync(SomeTenant, "gemini", estimatedInputTokens: 1000, maxOutputTokens: 1000, CancellationToken.None);

        Assert.True(check.Allowed);
        Assert.False(check.NearCap);
    }

    [Fact]
    public async Task Crossing_80_percent_of_the_cap_warns_but_still_allows_the_call()
    {
        // §R6: "80% warns" — spent + this call's own projection crosses the warn threshold, but
        // the call is not yet over the cap, so it must still be allowed to proceed.
        var pricing = new AiPricing(InputPer1M: 1_000_000m, OutputPer1M: 0m); // $1 per input token, for round numbers
        var guard = BuildGuard(spentUsd: 24.50m, pricing, capUsd: 30.00m); // 24.5 + 1 = 25.5 / 30 = 85%

        var check = await guard.CheckAsync(SomeTenant, "gemini", estimatedInputTokens: 1, maxOutputTokens: 0, CancellationToken.None);

        Assert.True(check.Allowed);
        Assert.True(check.NearCap);
    }

    [Fact]
    public async Task Exactly_at_the_cap_is_still_allowed_the_boundary_is_inclusive()
    {
        var pricing = new AiPricing(InputPer1M: 1_000_000m, OutputPer1M: 0m);
        var guard = BuildGuard(spentUsd: 29.00m, pricing, capUsd: 30.00m); // 29 + 1 = 30 == cap

        var check = await guard.CheckAsync(SomeTenant, "gemini", estimatedInputTokens: 1, maxOutputTokens: 0, CancellationToken.None);

        Assert.True(check.Allowed);
    }

    [Fact]
    public async Task One_cent_over_the_cap_is_refused()
    {
        // §R6: "100% returns a typed refusal, before the provider is ever called."
        var pricing = new AiPricing(InputPer1M: 1_000_000m, OutputPer1M: 0m);
        var guard = BuildGuard(spentUsd: 29.00m, pricing, capUsd: 30.00m);

        var check = await guard.CheckAsync(SomeTenant, "gemini", estimatedInputTokens: 2, maxOutputTokens: 0, CancellationToken.None); // 29 + 2 = 31 > 30

        Assert.False(check.Allowed);
    }

    [Fact]
    public async Task Already_over_the_cap_before_this_calls_own_projection_is_refused()
    {
        var pricing = new AiPricing(InputPer1M: 0m, OutputPer1M: 0m); // this call itself is free
        var guard = BuildGuard(spentUsd: 31.00m, pricing, capUsd: 30.00m);

        var check = await guard.CheckAsync(SomeTenant, "gemini", estimatedInputTokens: 0, maxOutputTokens: 0, CancellationToken.None);

        Assert.False(check.Allowed);
        Assert.Equal(31.00m, check.SpentUsd);
        Assert.Equal(0m, check.ProjectedUsd);
        Assert.Equal(30.00m, check.CapUsd);
    }

    [Fact]
    public async Task The_projected_cost_comes_from_the_real_pricing_table_not_a_guess()
    {
        // §R5: "Cost = tokens × price table from config, not hardcoded" — this proves the guard's
        // own projection is the pricing provider's real CostFor math, not an approximation.
        var pricing = new AiPricing(InputPer1M: 2.00m, OutputPer1M: 8.00m);
        var guard = BuildGuard(spentUsd: 0m, pricing, capUsd: 1000m);

        var check = await guard.CheckAsync(SomeTenant, "gemini", estimatedInputTokens: 500_000, maxOutputTokens: 250_000, CancellationToken.None);

        Assert.Equal(pricing.CostFor(500_000, 250_000), check.ProjectedUsd);
    }

    [Fact]
    public async Task An_unpriced_provider_projects_zero_cost_never_a_guess()
    {
        // §R5: "Unknown provider -> Zero. Fail safe: can't overcount, only undercount."
        var guard = BuildGuard(spentUsd: 0m, pricing: AiPricing.Zero, capUsd: 30.00m);

        var check = await guard.CheckAsync(SomeTenant, "a-provider-nobody-priced", estimatedInputTokens: 1_000_000, maxOutputTokens: 1_000_000, CancellationToken.None);

        Assert.Equal(0m, check.ProjectedUsd);
        Assert.True(check.Allowed);
    }

    // DISC-14: a provider's own request-count quota (e.g. a free-tier Gemini key's 20/day) is a
    // different constraint from dollar cost — a free-tier call is $0, so none of the cost-based
    // tests above would ever catch it.

    [Fact]
    public async Task Under_the_daily_request_quota_is_allowed_even_at_zero_cost()
    {
        var guard = BuildGuard(spentUsd: 0m, pricing: AiPricing.Zero, capUsd: 30.00m, requestsToday: 10, dailyRequestQuota: 20);

        var check = await guard.CheckAsync(SomeTenant, "gemini", estimatedInputTokens: 100, maxOutputTokens: 100, CancellationToken.None);

        Assert.True(check.Allowed);
        Assert.False(check.QuotaExceeded);
        Assert.Equal(10, check.RequestsToday);
        Assert.Equal(20, check.DailyRequestQuota);
    }

    [Fact]
    public async Task At_the_daily_request_quota_is_refused_even_though_the_dollar_cost_is_nowhere_near_the_cap()
    {
        // The exact real-world case that surfaced this: a free-tier key's 20/day limit hit while
        // the $30/week dollar guard alone would have happily allowed a ~$0.03 call.
        var guard = BuildGuard(spentUsd: 0.03m, pricing: AiPricing.Zero, capUsd: 30.00m, requestsToday: 20, dailyRequestQuota: 20);

        var check = await guard.CheckAsync(SomeTenant, "gemini", estimatedInputTokens: 100, maxOutputTokens: 100, CancellationToken.None);

        Assert.False(check.Allowed);
        Assert.True(check.QuotaExceeded);
    }

    [Fact]
    public async Task An_unconfigured_daily_quota_defaults_to_unbounded()
    {
        // ConfigurationAiBudgetOptionsProvider's own default (int.MaxValue) — an environment that
        // never set Ai:Budget:DailyRequestQuota (a paid-tier key, say) is never artificially capped.
        var guard = BuildGuard(spentUsd: 0m, pricing: AiPricing.Zero, capUsd: 30.00m, requestsToday: 10_000);

        var check = await guard.CheckAsync(SomeTenant, "gemini", estimatedInputTokens: 100, maxOutputTokens: 100, CancellationToken.None);

        Assert.True(check.Allowed);
        Assert.False(check.QuotaExceeded);
    }
}
