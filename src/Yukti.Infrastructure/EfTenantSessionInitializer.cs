using Microsoft.EntityFrameworkCore;
using Yukti.Application.Abstractions;
using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure;

/// <summary>
/// Same set_config('app.current_tenant_id', ...) call Yukti.Api's request
/// middleware and startup seeding block both already make manually — this
/// is the one shared implementation, using the same YuktiDbContext/
/// connection every other Scoped repository in the current request
/// shares, so the setting applies to every subsequent query on that
/// connection for the rest of the request.
/// </summary>
public sealed class EfTenantSessionInitializer : ITenantSessionInitializer
{
    private readonly YuktiDbContext _context;
    public EfTenantSessionInitializer(YuktiDbContext context) => _context = context;

    public async Task EstablishTenantContext(TenantId tenantId, CancellationToken ct)
    {
        if (_context.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
            await _context.Database.OpenConnectionAsync(ct);
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.current_tenant_id', {tenantId.Value.ToString()}, false)", ct);
    }
}
