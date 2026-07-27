using Yukti.Domain.SharedKernel;

namespace Yukti.Application.Abstractions;

/// <summary>
/// FR-TENANT-01/FR-DB-02 fallout, fixed here: Yukti.Api's per-request
/// middleware sets app.current_tenant_id from the JWT — but
/// self-registration mints a brand-new TenantId as part of the request
/// itself, with no JWT yet to derive it from. Without this, the INSERT of
/// that user's row violates RLS's WITH CHECK (the same session variable
/// governs both reads and writes). Same operation Program.cs's startup
/// seeding block already performs manually for the bootstrap admin;
/// this makes it available to any command handler that mints a new
/// tenant mid-request, not just startup code.
/// </summary>
public interface ITenantSessionInitializer
{
    Task EstablishTenantContext(TenantId tenantId, CancellationToken ct);
}
