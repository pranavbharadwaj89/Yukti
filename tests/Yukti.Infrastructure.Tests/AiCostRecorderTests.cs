using Xunit;
using Yukti.Application.Abstractions;
using Yukti.Application.Ai;
using Yukti.Domain.Ai;
using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure.Tests;

/// <summary>
/// D5-T9/§R5: real token accounting — every provider call writes an <see cref="AiCostRecord"/>
/// whose cost is tokens × the pricing table, never hardcoded or guessed. <see cref="AiCostRecorder"/>
/// depends only on <see cref="IAiCostRepository"/>/<see cref="IAiPricingProvider"/> (both
/// abstractions), so this exercises it directly rather than through the full engine loop —
/// <see cref="AiPlanningEngineTests"/> already proves it's called once per attempt.
/// </summary>
public sealed class AiCostRecorderTests
{
    private static readonly TenantId SomeTenant = TenantId.New();

    private sealed class RecordingRepository : IAiCostRepository
    {
        public List<AiCostRecord> Records { get; } = new();
        public Task Append(AiCostRecord record, CancellationToken ct) { Records.Add(record); return Task.CompletedTask; }
    }

    private sealed class FixedPricingProvider : IAiPricingProvider
    {
        private readonly AiPricing _pricing;
        public FixedPricingProvider(AiPricing pricing) => _pricing = pricing;
        public AiPricing GetFor(string provider) => _pricing;
    }

    [Fact]
    public async Task A_successful_attempt_is_recorded_with_cost_from_the_real_pricing_table()
    {
        var repository = new RecordingRepository();
        var pricing = new AiPricing(InputPer1M: 2.00m, OutputPer1M: 8.00m);
        var recorder = new AiCostRecorder(repository, new FixedPricingProvider(pricing));

        await recorder.RecordAsync(
            new AiCostRecordData(SomeTenant, "coder", "gemini", "v1", Attempt: 1, Succeeded: true,
                InputTokens: 100_000, OutputTokens: 50_000, TimeSpan.FromMilliseconds(5), DateTimeOffset.UtcNow),
            CancellationToken.None);

        var record = Assert.Single(repository.Records);
        Assert.Equal(SomeTenant, record.TenantId);
        Assert.Equal("coder", record.Capability);
        Assert.Equal("gemini", record.Provider);
        Assert.Equal(1, record.Attempt);
        Assert.True(record.Succeeded);
        Assert.False(record.BudgetRefused);
        Assert.Equal(pricing.CostFor(100_000, 50_000), record.CostUsd);
    }

    [Fact]
    public async Task A_failed_attempt_is_still_recorded_it_already_spent_real_tokens()
    {
        var repository = new RecordingRepository();
        var pricing = new AiPricing(InputPer1M: 1.00m, OutputPer1M: 1.00m);
        var recorder = new AiCostRecorder(repository, new FixedPricingProvider(pricing));

        await recorder.RecordAsync(
            new AiCostRecordData(SomeTenant, "coder", "gemini", "v1", Attempt: 1, Succeeded: false,
                InputTokens: 10_000, OutputTokens: 5_000, TimeSpan.FromMilliseconds(5), DateTimeOffset.UtcNow),
            CancellationToken.None);

        var record = Assert.Single(repository.Records);
        Assert.False(record.Succeeded);
        Assert.True(record.CostUsd > 0); // a failed validation still burned real provider tokens
    }

    [Fact]
    public async Task A_budget_refusal_is_recorded_at_zero_cost_never_priced_the_call_that_never_happened()
    {
        // §R6/§R10: the one reject worth a row of its own (distinct from the zero-cost guardrail
        // rejects, which record nothing at all) — the usage endpoint's "refusals by reason" needs
        // it, but it must never claim a cost for a call that was refused before it ever ran.
        var repository = new RecordingRepository();
        var pricing = new AiPricing(InputPer1M: 999m, OutputPer1M: 999m); // deliberately expensive — must be ignored
        var recorder = new AiCostRecorder(repository, new FixedPricingProvider(pricing));

        await recorder.RecordAsync(
            new AiCostRecordData(SomeTenant, "coder", "gemini", "v1", Attempt: 0, Succeeded: false,
                InputTokens: 0, OutputTokens: 0, TimeSpan.Zero, DateTimeOffset.UtcNow, BudgetRefused: true),
            CancellationToken.None);

        var record = Assert.Single(repository.Records);
        Assert.True(record.BudgetRefused);
        Assert.Equal(0m, record.CostUsd);
        Assert.Equal(0, record.Attempt);
    }

    [Fact]
    public async Task An_unpriced_provider_records_zero_cost_never_an_invented_number()
    {
        var repository = new RecordingRepository();
        var recorder = new AiCostRecorder(repository, new FixedPricingProvider(AiPricing.Zero));

        await recorder.RecordAsync(
            new AiCostRecordData(SomeTenant, "coder", "mystery-provider", "v1", Attempt: 1, Succeeded: true,
                InputTokens: 1_000_000, OutputTokens: 1_000_000, TimeSpan.FromMilliseconds(5), DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.Equal(0m, Assert.Single(repository.Records).CostUsd);
    }
}
