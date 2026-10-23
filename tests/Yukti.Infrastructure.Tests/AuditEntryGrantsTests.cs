using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;
using Yukti.Domain.Auditing;
using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure.Tests;

/// <summary>
/// FR-AUDIT-03 live evidence: "Direct SQL UPDATE/DELETE against the table
/// fails with a permissions error even from the app's own connection."
/// Runs against the real database the AddAuditEntryGrants +
/// AddYuktiAppRuntimeRole migrations were applied to — Skip()s rather
/// than fails when no connection string is configured, since this
/// genuinely needs live infrastructure this repo's other integration
/// tests don't (everything else uses Yukti.Infrastructure.InMemory).
///
/// RESOLVED (2026-07-27): earlier in this session, these tests ran
/// against the app's connection as "pranav" — the table owner — and
/// confirmed REVOKE has no effect on an owner (Postgres/CockroachDB
/// owners retain implicit DML privileges independent of GRANT/REVOKE).
/// ConnectionStrings:YuktiRuntime now points at "yukti_app", a distinct,
/// non-owner role created by the AddYuktiAppRuntimeRole migration with
/// only SELECT/INSERT on audit_entries — this test asserts the FR's
/// actual target behavior against that role, not the owner-bypass gap.
///
/// D6-T1/D6-T2 fallout (2026-10-21): discovered the first time this file
/// actually ran in CI (build-and-test previously had no Postgres service
/// at all, D6-T1) — a direct UPDATE against audit_entries can surface
/// Postgres error 22P02 (invalid uuid cast) instead of 42501 when
/// app.current_tenant_id is left unset/stale, because yukti_app's SELECT
/// grant on this table lets the WHERE-clause row scan reach the RLS
/// policy's `current_setting('app.current_tenant_id', true)::uuid` cast
/// before the missing-UPDATE-privilege check ever gets evaluated — and
/// TenantIsolationTests.cs shares this file's exact connection string
/// (ConnectionStrings:YuktiRuntime), so Npgsql's connection pool can hand
/// this test a physical connection whose session-level GUC was left in
/// whatever state a previous test's own set_config call put it in.
/// audit_entries' own policy is permissive ("TenantId IS NULL OR ..."),
/// so any valid tenant context makes the cast succeed and the row
/// visible regardless of its value — establishing one deterministically,
/// the same way every other RLS-aware live-DB test in this project
/// already does, is what actually makes the GRANT-level 42501 this test
/// means to prove the only possible outcome.
/// </summary>
public sealed class AuditEntryGrantsTests
{
    private static string? GetConnectionString()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<AuditEntryGrantsTests>()
            .AddEnvironmentVariables()
            .Build();
        return config.GetConnectionString("YuktiRuntime");
    }

    [Fact]
    public async Task Direct_UPDATE_against_audit_entries_fails_with_a_permissions_error()
    {
        var connectionString = GetConnectionString();
        if (connectionString is null)
        {
            return; // Skip: no live DB configured in this environment.
        }

        var options = new DbContextOptionsBuilder<YuktiDbContext>().UseNpgsql(connectionString).Options;
        await using var context = new YuktiDbContext(options);
        await new EfTenantSessionInitializer(context).EstablishTenantContext(TenantId.New(), CancellationToken.None);

        var entry = AuditEntry.Capture("SmokeTestCommand", tenantId: null, succeeded: true, failureReason: null,
            new Dictionary<string, object?> { ["Note"] = "AuditEntryGrantsTests seed row" });
        context.Add(entry);
        await context.SaveChangesAsync(); // yukti_app has INSERT — this must still succeed

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE audit_entries SET \"Succeeded\" = false WHERE \"Id\" = {entry.Id.Value}"));

        Assert.Equal("42501", ex.SqlState); // insufficient_privilege
    }

    [Fact]
    public async Task Direct_DELETE_against_audit_entries_fails_with_a_permissions_error()
    {
        var connectionString = GetConnectionString();
        if (connectionString is null)
        {
            return; // Skip: no live DB configured in this environment.
        }

        var options = new DbContextOptionsBuilder<YuktiDbContext>().UseNpgsql(connectionString).Options;
        await using var context = new YuktiDbContext(options);
        await new EfTenantSessionInitializer(context).EstablishTenantContext(TenantId.New(), CancellationToken.None);

        var entry = AuditEntry.Capture("SmokeTestCommand", tenantId: null, succeeded: true, failureReason: null,
            new Dictionary<string, object?> { ["Note"] = "AuditEntryGrantsTests seed row" });
        context.Add(entry);
        await context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM audit_entries WHERE \"Id\" = {entry.Id.Value}"));

        Assert.Equal("42501", ex.SqlState); // insufficient_privilege
    }
}
