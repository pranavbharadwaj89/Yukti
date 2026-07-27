using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Yukti.Infrastructure;

/// <summary>
/// FR-AUDIT-03 fallout, fixed here: `dotnet ef` design-time tooling
/// otherwise builds the full Yukti.Api host from Program.cs to discover
/// YuktiDbContext — which means it picks up
/// ConnectionStrings:YuktiRuntime (the non-owner "yukti_app"/"yukti_worker"
/// roles) the exact same way the running app does. Those roles can't
/// run DDL (ALTER TABLE, CREATE POLICY, GRANT), so `dotnet ef migrations
/// add`/`database update` silently broke the moment that fallback was
/// introduced. This factory is what EF's tooling prefers over building
/// the whole host, and explicitly, always resolves ConnectionStrings:Yukti
/// (the owner-privileged "pranav" connection) — migrations should never
/// depend on Program.cs's runtime connection-selection logic.
/// </summary>
public sealed class YuktiDbContextFactory : IDesignTimeDbContextFactory<YuktiDbContext>
{
    public YuktiDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets("6b746e6b-ec5a-4995-a73e-b26cd66270e2") // Yukti.Api's UserSecretsId — same secrets store
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Yukti")
            ?? throw new InvalidOperationException(
                "Missing ConnectionStrings:Yukti for design-time migration tooling. " +
                "Set it via `dotnet user-secrets set \"ConnectionStrings:Yukti\" \"...\" --project src/Yukti.Api`.");

        var optionsBuilder = new DbContextOptionsBuilder<YuktiDbContext>().UseNpgsql(connectionString);
        return new YuktiDbContext(optionsBuilder.Options);
    }
}
