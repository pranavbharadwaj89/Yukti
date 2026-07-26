using Yukti.Domain.SharedKernel;

namespace Yukti.Application.Abstractions;

/// <summary>
/// FR-TENANT-02: TenantId is sourced only from here, populated only from
/// the authenticated principal — never a header, query param, or
/// client-supplied value. Concrete implementation lives in Yukti.Api
/// (reads the JWT "tenant" claim via IHttpContextAccessor), since it's an
/// ASP.NET Core concern; Application only depends on this abstraction.
/// Null means no authenticated tenant context (unauthenticated requests,
/// or process-startup seeding outside any HTTP request) — every
/// tenant-scoped repository query treats null as "match nothing" rather
/// than "match everything," so a missing tenant context fails closed.
/// </summary>
public interface ITenantContextAccessor
{
    TenantId? CurrentTenantId { get; }
}

/// <summary>
/// FR-TENANT-01's third, independent enforcement layer (application-service
/// assertion) — deliberately redundant with the repository query filter
/// (Layer 1) and database RLS (Layer 2). Called explicitly in command
/// handlers and read endpoints right after fetching a tenant-scoped
/// aggregate, so that if either of the other two layers has a bug, this
/// one still blocks cross-tenant access on its own.
/// </summary>
public interface ITenantGuard
{
    void EnsureAccessible(TenantId resourceTenantId);
}

public sealed class TenantGuard : ITenantGuard
{
    private readonly ITenantContextAccessor _accessor;

    public TenantGuard(ITenantContextAccessor accessor) => _accessor = accessor;

    public void EnsureAccessible(TenantId resourceTenantId)
    {
        if (_accessor.CurrentTenantId is null || _accessor.CurrentTenantId != resourceTenantId)
            throw new ForbiddenException($"Resource belongs to a different tenant than {_accessor.CurrentTenantId}.");
    }
}
