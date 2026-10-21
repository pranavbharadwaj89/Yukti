using Yukti.Domain.SharedKernel;

namespace Yukti.Application.Ai;

/// <summary>
/// D5-T9/§R5: <c>Cost = tokens × price table from config, not hardcoded</c>. Unknown provider →
/// <see cref="Zero"/> — never invent a cost for a provider nobody priced (fail safe: an
/// unrecognized identifier can't overcount, only undercount, against the budget).
/// </summary>
public sealed record AiPricing(decimal InputPer1M, decimal OutputPer1M)
{
    public static readonly AiPricing Zero = new(0m, 0m);

    public decimal CostFor(int inputTokens, int outputTokens) =>
        inputTokens / 1_000_000m * InputPer1M + outputTokens / 1_000_000m * OutputPer1M;
}

/// <summary>Resolves a provider's $/1M-token rates from configuration (<c>Ai:Pricing:&lt;provider&gt;</c>).</summary>
public interface IAiPricingProvider
{
    AiPricing GetFor(string provider);
}

/// <summary>D5-T9/§R6: the weekly cap and warn threshold, both config-driven (never a literal in the engine).
/// <see cref="DailyRequestQuota"/> is DISC-14: a provider's own *request-count* ceiling (e.g. a
/// free-tier Gemini key's 20-calls/day limit) is a completely different constraint from the dollar
/// cap above — a free-tier key costs $0 per call, so the dollar guard alone would never catch it.
/// Defaults to <see cref="int.MaxValue"/> (no enforcement) so an unconfigured/paid-tier environment
/// is never artificially constrained; only set explicitly where the underlying key actually has
/// one.</summary>
public interface IAiBudgetOptionsProvider
{
    decimal WeeklyCapUsd { get; }
    double WarnThreshold { get; }
    int DailyRequestQuota { get; }
}

/// <summary>
/// Result of the engine's own worst-case-before-calling check (§R6, extended by DISC-14 for request
/// count). <see cref="Allowed"/> false means the call must never reach the provider — the caller
/// throws <see cref="AiBudgetExceededException"/> (<see cref="QuotaExceeded"/> false) or <see
/// cref="AiRequestQuotaExceededException"/> (<see cref="QuotaExceeded"/> true) and records a <see
/// cref="Domain.Ai.AiCostRecord.ForBudgetRefusal"/> row, never spends anything.
/// </summary>
public sealed record AiBudgetCheck(
    bool Allowed, bool NearCap, decimal SpentUsd, decimal ProjectedUsd, decimal CapUsd,
    bool QuotaExceeded = false, int RequestsToday = 0, int DailyRequestQuota = int.MaxValue);

/// <summary>D5-T9: the one thing <see cref="Yukti.Application.Ai.IAiPlanningEngine"/> needs to enforce §R6 — kept separate from <see cref="IAiCostRecorder"/> (write-after-the-fact) since this is a read-before-calling check.</summary>
public interface IAiUsageQuery
{
    /// <summary>Week-to-date totals for <paramref name="tenantId"/>, since <paramref name="since"/> (the current ISO week's Monday 00:00 UTC — see <c>AiWeek.StartOfCurrentWeekUtc</c>).</summary>
    Task<AiUsageSummary> GetUsageSince(TenantId tenantId, DateTimeOffset since, CancellationToken ct);
}

public sealed record AiCapabilityUsage(string Capability, int Calls, int Retries, int InputTokens, int OutputTokens, decimal CostUsd);

public sealed record AiUsageSummary(
    decimal TotalCostUsd, int TotalInputTokens, int TotalOutputTokens, int BudgetRefusals,
    IReadOnlyList<AiCapabilityUsage> ByCapability);

/// <summary>
/// D5-T9: the single thing <see cref="IAiPlanningEngine"/> depends on for §R6 — composed, in
/// Infrastructure, from <see cref="IAiUsageQuery"/> + <see cref="IAiPricingProvider"/> +
/// <see cref="IAiBudgetOptionsProvider"/>. One port, not three, so every existing engine test
/// only needs one new fake (almost always "always allowed") instead of three.
/// </summary>
public interface IAiBudgetGuard
{
    Task<AiBudgetCheck> CheckAsync(TenantId tenantId, string provider, int estimatedInputTokens, int maxOutputTokens, CancellationToken ct);
}

/// <summary>Raised when the budget guard refuses a call before it ever reaches the provider — distinct from <see cref="AiUnavailableException"/> (provider failed) and <see cref="AiDisabledException"/> (switch is off; that one wins, see D5-T2).</summary>
public sealed class AiBudgetExceededException : Exception
{
    public AiBudgetExceededException(decimal spentUsd, decimal projectedUsd, decimal capUsd)
        : base($"AI budget reached: ${spentUsd:F2} already spent this week, plus a projected ${projectedUsd:F2} for " +
               $"this call, would exceed the ${capUsd:F2} weekly cap. The rest of Yukti keeps working — this only blocks AI generation.")
    {
    }
}

/// <summary>
/// DISC-14: raised when the provider's own daily *request-count* quota (not a dollar amount) would
/// be exceeded — e.g. a free-tier Gemini key's 20-calls/day ceiling, confirmed the hard way via a
/// real HTTP 429 `RESOURCE_EXHAUSTED` during D5-T8's eval harness, which the dollar-based guard
/// alone had no way to see coming (a free-tier call costs $0, so it was never near the $ cap).
/// Distinct from <see cref="AiBudgetExceededException"/> so the two constraints are never confused
/// in logs or the usage endpoint — same "the rest of Yukti keeps working" guarantee either way.
/// </summary>
public sealed class AiRequestQuotaExceededException : Exception
{
    public AiRequestQuotaExceededException(int requestsToday, int dailyRequestQuota)
        : base($"AI request quota reached: {requestsToday} of {dailyRequestQuota} requests already made today " +
               "(shared with every other caller of this key, including Sarvaghna's own commit/push-hook AI reviews). " +
               "The rest of Yukti keeps working — this only blocks AI generation.")
    {
    }
}

/// <summary>
/// DISC-14: tracks how many real provider requests have been made today, against a daily quota the
/// provider itself enforces (a free-tier Gemini key's 20/day) — global, not tenant-scoped, since the
/// underlying constraint is per API *key*, shared across every tenant and even across runtimes
/// (Sarvagna's .NET calls and Sarvaghna's own Node commit/push-hook calls draw from the identical
/// quota). A real provider call, whether it succeeds or fails, counts; a call this guard itself
/// refused before ever reaching the provider does not (see <see
/// cref="Domain.Ai.AiCostRecord.ForBudgetRefusal"/>'s own equivalent rule for cost records).
/// </summary>
public interface IAiRequestQuotaLedger
{
    Task<int> CountTodayAsync(CancellationToken ct);
    Task RecordAsync(string source, string capability, CancellationToken ct);
}

/// <summary>D5-T9: the calendar-week boundary every budget figure is measured against — matches <c>plan/AI-BUDGET.md</c>'s own framing ("Week of Mon 12 Oct 2026"), not a rolling 7 days.</summary>
public static class AiWeek
{
    public static DateTimeOffset StartOfCurrentWeekUtc(DateTimeOffset nowUtc)
    {
        var daysSinceMonday = ((int)nowUtc.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return new DateTimeOffset(nowUtc.UtcDateTime.Date, TimeSpan.Zero).AddDays(-daysSinceMonday);
    }
}
