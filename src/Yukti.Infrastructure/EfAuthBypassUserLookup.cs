using Microsoft.EntityFrameworkCore;
using Yukti.Application.Abstractions;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure;

/// <summary>
/// Backs IAuthBypassUserLookup with its own, separately-configured
/// YuktiDbContext connected via the BYPASSRLS "yukti_worker" role —
/// deliberately NOT the request-scoped, RLS-enforced YuktiDbContext every
/// other repository in this project shares, since the whole point is
/// seeing a user row regardless of tenant context (see the interface's
/// own doc comment for why). Constructed once with its own
/// DbContextOptions rather than resolved from DI, since it needs a
/// distinct connection string from the app's normal per-request context.
/// </summary>
public sealed class EfAuthBypassUserLookup : IAuthBypassUserLookup
{
    private readonly DbContextOptions<YuktiDbContext> _options;

    public EfAuthBypassUserLookup(string bypassConnectionString) =>
        _options = new DbContextOptionsBuilder<YuktiDbContext>().UseNpgsql(bypassConnectionString).Options;

    public async Task<User?> GetByEmail(string email, CancellationToken ct)
    {
        await using var context = new YuktiDbContext(_options);
        return await context.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
    }

    public async Task<User?> GetById(UserId id, CancellationToken ct)
    {
        await using var context = new YuktiDbContext(_options);
        return await context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
    }
}
