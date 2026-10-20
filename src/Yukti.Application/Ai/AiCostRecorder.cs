using Yukti.Application.Abstractions;
using Yukti.Domain.Ai;

namespace Yukti.Application.Ai;

/// <summary>
/// D5-T9: the real <see cref="IAiCostRecorder"/> — the persisted implementation D5-T3's own doc
/// comment on <see cref="IAiCostRecorder"/> named from the start, replacing the logging-only
/// stand-in. Depends only on abstractions (<see cref="IAiCostRepository"/>, <see
/// cref="IAiPricingProvider"/>), so this one class works unchanged over either the EF-backed or
/// in-memory repository — no separate Infrastructure-specific recorder needed.
/// </summary>
public sealed class AiCostRecorder : IAiCostRecorder
{
    private readonly IAiCostRepository _repository;
    private readonly IAiPricingProvider _pricing;

    public AiCostRecorder(IAiCostRepository repository, IAiPricingProvider pricing)
    {
        _repository = repository;
        _pricing = pricing;
    }

    public Task RecordAsync(AiCostRecordData data, CancellationToken ct)
    {
        var record = data.BudgetRefused
            ? AiCostRecord.ForBudgetRefusal(data.TenantId, data.Capability, data.Provider)
            : AiCostRecord.ForAttempt(data.TenantId, data.Capability, data.Provider, data.PromptVersion, data.Attempt,
                data.Succeeded, data.InputTokens, data.OutputTokens,
                _pricing.GetFor(data.Provider).CostFor(data.InputTokens, data.OutputTokens));

        return _repository.Append(record, ct);
    }
}
