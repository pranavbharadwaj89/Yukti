using Yukti.Domain.Execution;
using Yukti.Domain.SharedKernel;

namespace Yukti.Application.Abstractions;

/// <summary>
/// FR-API-02: the TriggerFlowRunCommand-backed endpoint honors an
/// Idempotency-Key header — a retried request with the same key (scoped
/// per tenant, since keys are client-chosen and two different tenants may
/// pick the same string) returns the original run's result rather than
/// triggering a second execution.
/// </summary>
public interface IIdempotencyStore
{
    Task<FlowRunId?> TryGetResult(TenantId tenantId, string idempotencyKey, CancellationToken ct);
    Task Record(TenantId tenantId, string idempotencyKey, FlowRunId runId, CancellationToken ct);
}
