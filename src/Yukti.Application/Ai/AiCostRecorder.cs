using Yukti.Application.Abstractions;
using Yukti.Domain.Ai;

namespace Yukti.Application.Ai;

/// <summary>
/// D5-T9: the real <see cref="IAiCostRecorder"/> — the persisted implementation D5-T3's own doc
/// comment on <see cref="IAiCostRecorder"/> named from the start, replacing the logging-only
/// stand-in. Depends only on abstractions (<see cref="IAiCostRepository"/>, <see
/// cref="IAiPricingProvider"/>, <see cref="IAiRequestQuotaLedger"/>), so this one class works
/// unchanged over either the EF-backed or in-memory repository — no separate
/// Infrastructure-specific recorder needed.
///
/// DISC-14: every real attempt (succeeded or failed — <see cref="AiCostRecordData.BudgetRefused"/>
/// false) also records one line to the shared request-count ledger, at the exact same granularity
/// §R5's own cost-per-attempt row already uses — a budget refusal never reached the provider, so
/// it counts toward neither.
/// </summary>
public sealed class AiCostRecorder : IAiCostRecorder
{
    private const string Source = "sarvagna-dotnet";

    private readonly IAiCostRepository _repository;
    private readonly IAiPricingProvider _pricing;
    private readonly IAiRequestQuotaLedger _quotaLedger;

    public AiCostRecorder(IAiCostRepository repository, IAiPricingProvider pricing, IAiRequestQuotaLedger quotaLedger)
    {
        _repository = repository;
        _pricing = pricing;
        _quotaLedger = quotaLedger;
    }

    public async Task RecordAsync(AiCostRecordData data, CancellationToken ct)
    {
        var record = data.BudgetRefused
            ? AiCostRecord.ForBudgetRefusal(data.TenantId, data.Capability, data.Provider)
            : AiCostRecord.ForAttempt(data.TenantId, data.Capability, data.Provider, data.PromptVersion, data.Attempt,
                data.Succeeded, data.InputTokens, data.OutputTokens,
                _pricing.GetFor(data.Provider).CostFor(data.InputTokens, data.OutputTokens));

        await _repository.Append(record, ct);

        if (!data.BudgetRefused)
        {
            await _quotaLedger.RecordAsync(Source, data.Capability, ct);
        }
    }
}
