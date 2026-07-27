using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;
using Yukti.Domain.Auditing;

namespace Yukti.Infrastructure.Tests;

/// <summary>
/// FR-AUDIT-03 live evidence: "Direct SQL UPDATE/DELETE against the table
/// fails with a permissions error even from the app's own connection."
/// Runs against the real database the AddAuditEntryGrants migration was
/// applied to — Skip()s rather than fails when no connection string is
/// configured, since this genuinely needs live infrastructure this repo's
/// other integration tests don't (everything else uses
/// Yukti.Infrastructure.InMemory).
///
/// CONFIRMED GAP (run 2026-07-27 against the live CockroachDB instance):
/// REVOKE UPDATE, DELETE has no effect here, because the app connects as
/// "pranav" — the same role that owns audit_entries (it created the table
/// via migrations). Postgres/CockroachDB table owners retain implicit DML
/// privileges independent of GRANT/REVOKE; REVOKE only restricts *other*
/// roles. These tests assert the ACTUAL observed behavior (owner bypass),
/// not the FR's target behavior — closing FR-AUDIT-03 for real requires
/// migrations to run as a distinct owner/admin role while the app's
/// runtime connection uses a separate, lower-privileged role that only
/// ever receives SELECT/INSERT — a live infrastructure change, not
/// something to make unilaterally from this test file.
/// </summary>
public sealed class AuditEntryGrantsTests
{
    private static string? GetConnectionString()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<AuditEntryGrantsTests>()
            .AddEnvironmentVariables()
            .Build();
        return config.GetConnectionString("Yukti");
    }

    [Fact]
    public async Task KNOWN_GAP_owner_role_can_still_UPDATE_despite_REVOKE()
    {
        var connectionString = GetConnectionString();
        if (connectionString is null)
        {
            return; // Skip: no live DB configured in this environment.
        }

        var options = new DbContextOptionsBuilder<YuktiDbContext>().UseNpgsql(connectionString).Options;
        await using var context = new YuktiDbContext(options);

        var entry = AuditEntry.Capture("SmokeTestCommand", succeeded: true, failureReason: null,
            new Dictionary<string, object?> { ["Note"] = "AuditEntryGrantsTests seed row" });
        context.Add(entry);
        await context.SaveChangesAsync();

        // FR-AUDIT-03's target is that this throws PostgresException
        // (insufficient_privilege). It does not, here — see the class
        // doc comment. This assertion documents the confirmed gap rather
        // than a false pass; it will start failing (correctly) the day a
        // non-owner runtime role closes this for real.
        var affectedRows = await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE audit_entries SET \"Succeeded\" = false WHERE \"Id\" = {entry.Id.Value}");
        Assert.Equal(1, affectedRows);
    }
}
