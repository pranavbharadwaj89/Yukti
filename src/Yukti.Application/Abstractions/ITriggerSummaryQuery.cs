using Yukti.Domain.Scheduling;
using Yukti.Domain.SharedKernel;

namespace Yukti.Application.Abstractions;

// FR-REPO-02 split, mirroring IProjectSummaryQuery — list operations live
// here, never on ITriggerRepository.
public sealed record TriggerSummary(
    TriggerId Id, FlowId FlowId, TriggerKind Kind, bool IsEnabled, DateTimeOffset? LastFiredAt,
    string? CronExpression, string? WebhookPath, string? WatchPath);

public interface ITriggerSummaryQuery
{
    Task<IReadOnlyList<TriggerSummary>> ListByTenant(TenantId tenantId, CancellationToken ct);
}
