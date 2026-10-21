using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;
using Yukti.Application.Abstractions;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure.Tests;

/// <summary>
/// D6-T1: proves FR-TENANT-01's tenant isolation actually holds against the real database, not
/// just that the policies exist on paper. <c>AddRowLevelSecurity</c>'s own migration documents
/// three independent layers — Layer 1: each repository's own <c>WHERE TenantId = ...</c>, Layer 2:
/// the Postgres RLS policy (<c>FORCE ROW LEVEL SECURITY</c>, so even the table owner's connection
/// is bound by it), Layer 3: <c>TenantGuard</c> at the command-handler level. This file proves
/// Layer 2 specifically, independent of the other two — the one layer that still holds even if a
/// repository method or handler forgets its own tenant filter.
///
/// Runs against the real Postgres the migrations were applied to (same skip-if-unconfigured
/// pattern as <see cref="AuditEntryGrantsTests"/>) — RLS cannot be proven against
/// <c>Yukti.Infrastructure.InMemory</c>, which has no database to enforce anything.
/// </summary>
public sealed class TenantIsolationTests
{
    private static string? GetConnectionString()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<TenantIsolationTests>()
            .AddEnvironmentVariables()
            .Build();
        return config.GetConnectionString("YuktiRuntime") ?? config.GetConnectionString("Yukti");
    }

    private sealed class FixedTenantContextAccessor : ITenantContextAccessor
    {
        public FixedTenantContextAccessor(TenantId tenantId) => CurrentTenantId = tenantId;
        public TenantId? CurrentTenantId { get; }
    }

    private static async Task<YuktiDbContext> OpenSessionAs(string connectionString, TenantId tenantId, CancellationToken ct)
    {
        var options = new DbContextOptionsBuilder<YuktiDbContext>().UseNpgsql(connectionString).Options;
        var context = new YuktiDbContext(options);
        await new EfTenantSessionInitializer(context).EstablishTenantContext(tenantId, ct);
        return context;
    }

    /// <summary>A plain Npgsql scalar count — deliberately not an EF LINQ query, so nothing in the
    /// C# query itself could be hiding the row; only the database's own RLS policy can.</summary>
    private static async Task<long> RawCountAsync(YuktiDbContext context, string sql, Guid id, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("id", id);
        var result = await cmd.ExecuteScalarAsync(ct);
        return (long)result!;
    }

    private static PostgresException? FindPostgresException(Exception ex) => ex switch
    {
        PostgresException pg => pg,
        { InnerException: { } inner } => FindPostgresException(inner),
        _ => null,
    };

    [Fact]
    public async Task Tenant_B_cannot_read_tenant_As_flow_through_the_real_repository()
    {
        var connectionString = GetConnectionString();
        if (connectionString is null) return; // Skip: no live DB configured in this environment.

        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var ct = CancellationToken.None;

        FlowId flowId;
        await using (var asA = await OpenSessionAs(connectionString, tenantA, ct))
        {
            var flow = Flow.CreateDraft("D6-T1 tenant A's flow", null, tenantA, new UserId(Guid.NewGuid()));
            flowId = flow.Id;
            asA.Add(flow);
            await asA.SaveChangesAsync(ct);
        }

        await using var asB = await OpenSessionAs(connectionString, tenantB, ct);
        var repo = new EfFlowRepository(asB, new FixedTenantContextAccessor(tenantB));

        var result = await repo.GetById(flowId, ct);

        Assert.Null(result); // not "forbidden" — genuinely invisible, same as it not existing
    }

    [Fact]
    public async Task Tenant_B_cannot_see_tenant_As_flow_even_with_no_application_level_filter_at_all()
    {
        // Layer 2 alone: a raw, unparameterized-by-tenant query — proves the database itself
        // refuses the row, independent of whether the C# repository's own WHERE is even present.
        var connectionString = GetConnectionString();
        if (connectionString is null) return;

        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var ct = CancellationToken.None;

        FlowId flowId;
        await using (var asA = await OpenSessionAs(connectionString, tenantA, ct))
        {
            var flow = Flow.CreateDraft("D6-T1 tenant A's flow (raw query proof)", null, tenantA, new UserId(Guid.NewGuid()));
            flowId = flow.Id;
            asA.Add(flow);
            await asA.SaveChangesAsync(ct);
        }

        await using var asB = await OpenSessionAs(connectionString, tenantB, ct);
        var rawMatch = await RawCountAsync(asB, "SELECT count(*) FROM flows WHERE \"Id\" = @id", flowId.Value, ct);

        Assert.Equal(0, rawMatch);
    }

    [Fact]
    public async Task Tenant_B_cannot_read_tenant_As_flow_run_or_its_step_results()
    {
        var connectionString = GetConnectionString();
        if (connectionString is null) return;

        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var ct = CancellationToken.None;

        FlowRunId runId;
        await using (var asA = await OpenSessionAs(connectionString, tenantA, ct))
        {
            var flow = Flow.CreateDraft("D6-T1 tenant A's flow (run proof)", null, tenantA, new UserId(Guid.NewGuid()));
            asA.Add(flow);
            await asA.SaveChangesAsync(ct);

            var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantA);
            run.Start();
            run.RecordStepResult(new StepResult(
                FlowStepId.New(), "D6-T1 seed step", ModuleKind.Custom("api"), "request",
                StepStatus.Passed, TimeSpan.FromMilliseconds(1)));
            runId = run.Id;
            asA.Add(run);
            await asA.SaveChangesAsync(ct);
        }

        await using var asB = await OpenSessionAs(connectionString, tenantB, ct);
        var repo = new EfFlowRunRepository(asB, new FixedTenantContextAccessor(tenantB));

        var result = await repo.GetById(runId, ct);
        Assert.Null(result);

        var stepResultCount = await RawCountAsync(asB, "SELECT count(*) FROM step_results WHERE \"FlowRunId\" = @id", runId.Value, ct);
        Assert.Equal(0, stepResultCount);
    }

    [Fact]
    public async Task Writing_a_row_tagged_for_a_different_tenant_than_the_current_session_is_rejected_by_the_database()
    {
        // The write half of "cannot read/write" — AddRowLevelSecurity's own migration comment:
        // "No separate WITH CHECK... the same expression governs reads and writes symmetrically.
        // A write that would violate the policy is rejected at the database."
        var connectionString = GetConnectionString();
        if (connectionString is null) return;

        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var ct = CancellationToken.None;

        await using var asA = await OpenSessionAs(connectionString, tenantA, ct);
        var flowIllegallyTaggedForB = Flow.CreateDraft("D6-T1 illegally tagged for tenant B", null, tenantB, new UserId(Guid.NewGuid()));
        asA.Add(flowIllegallyTaggedForB);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => asA.SaveChangesAsync(ct));
        var pgEx = FindPostgresException(ex);
        Assert.NotNull(pgEx);
        Assert.Equal("42501", pgEx!.SqlState); // insufficient_privilege — the RLS policy rejected the write
    }
}
