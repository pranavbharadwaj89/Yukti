using Yukti.Domain.SharedKernel;

namespace Yukti.Domain.Ai;

/// <summary>
/// D5-T9 — the real, persisted implementation of the seam D5-T3's <c>IAiCostRecorder</c> doc
/// comment named from the start ("defining the seam now means D5-T9 is a new implementation of
/// this one interface, not a change to the planning engine itself"). Append-only, like <see
/// cref="Yukti.Domain.Auditing.AuditEntry"/> — not an AggregateRoot, no method mutates an existing
/// record, no repository exposes Update/Delete.
///
/// Token-economy spec §R1's own invariant stays true here: a guardrail reject never reaches this
/// type at all (nothing was spent, nothing to record). The *one* exception is
/// <see cref="ForBudgetRefusal"/> — §R10 asks the usage endpoint to show "refusals by reason",
/// and a budget refusal is the one reject worth a zero-cost row of its own, distinct from both a
/// real attempt and from the guardrail rejects that still record nothing.
/// </summary>
public sealed class AiCostRecord
{
    public AiCostRecordId Id { get; }
    public TenantId TenantId { get; }
    public string Capability { get; }
    public string Provider { get; }
    public string PromptVersion { get; }
    /// <summary>1-based for a real attempt; 0 for a budget refusal (no attempt was made).</summary>
    public int Attempt { get; }
    public bool Succeeded { get; }
    public bool BudgetRefused { get; }
    public int InputTokens { get; }
    public int OutputTokens { get; }
    public decimal CostUsd { get; }
    public DateTimeOffset OccurredAt { get; }

    private AiCostRecord(
        AiCostRecordId id, TenantId tenantId, string capability, string provider, string promptVersion,
        int attempt, bool succeeded, bool budgetRefused, int inputTokens, int outputTokens, decimal costUsd,
        DateTimeOffset occurredAt)
    {
        Id = id;
        TenantId = tenantId;
        Capability = capability;
        Provider = provider;
        PromptVersion = promptVersion;
        Attempt = attempt;
        Succeeded = succeeded;
        BudgetRefused = budgetRefused;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        CostUsd = costUsd;
        OccurredAt = occurredAt;
    }

    public static AiCostRecord ForAttempt(
        TenantId tenantId, string capability, string provider, string promptVersion,
        int attempt, bool succeeded, int inputTokens, int outputTokens, decimal costUsd) =>
        new(AiCostRecordId.New(), tenantId, capability, provider, promptVersion, attempt, succeeded, budgetRefused: false,
            inputTokens, outputTokens, costUsd, DateTimeOffset.UtcNow);

    public static AiCostRecord ForBudgetRefusal(TenantId tenantId, string capability, string provider) =>
        new(AiCostRecordId.New(), tenantId, capability, provider, promptVersion: "", attempt: 0, succeeded: false,
            budgetRefused: true, inputTokens: 0, outputTokens: 0, costUsd: 0m, DateTimeOffset.UtcNow);
}
