using Yukti.Application.Abstractions;
using Yukti.Domain.SharedKernel;

namespace Yukti.Worker;

/// <summary>
/// Yukti.Api's ITenantContextAccessor reads a JWT claim off HttpContext —
/// there is no HttpContext here. This process's background jobs instead
/// process one tenant's work item at a time within a DI scope (one trigger
/// tick, one outbox event), so the tenant is set explicitly by that caller
/// right after creating the scope, then read by every tenant-filtered
/// repository for the rest of that scope's lifetime — same contract as
/// HttpContextTenantAccessor, just a settable source instead of a claim.
/// </summary>
public sealed class AmbientTenantContextAccessor : ITenantContextAccessor
{
    public TenantId? CurrentTenantId { get; set; }
}
