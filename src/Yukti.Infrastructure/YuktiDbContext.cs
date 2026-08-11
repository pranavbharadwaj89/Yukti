using Microsoft.EntityFrameworkCore;
using Yukti.Domain.ApiTesting;
using Yukti.Domain.Auditing;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.Configurations;
using Yukti.Infrastructure.ReadModels;

namespace Yukti.Infrastructure;

/// <summary>
/// One DbContext, mapping directly onto the aggregates FR-DB-01 names:
/// flows, flow_steps, flow_runs, step_results, retry_attempts,
/// module_registrations, module_action_entries, users, roles. Scoped
/// lifetime (one per request) — never Singleton, unlike the in-memory
/// repositories it replaces, which relied on Singleton for their
/// process-lifetime storage.
/// </summary>
public sealed class YuktiDbContext : DbContext
{
    public YuktiDbContext(DbContextOptions<YuktiDbContext> options) : base(options) { }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        SyncStepResultTenantIds();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        SyncStepResultTenantIds();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // FR-OPS-04 prep: step_results.TenantId is a shadow property (see
    // FlowRunConfiguration's doc comment) — StepResult itself has no tenant
    // concept, so nothing else ever sets it. New StepResults are always
    // added to an already-tracked FlowRun (FlowEngine loads the aggregate
    // via IFlowRunRepository before mutating it), so that FlowRun's
    // TenantId is reliably available here at save time.
    private void SyncStepResultTenantIds()
    {
        var flowRunTenantsById = ChangeTracker.Entries<FlowRun>()
            .ToDictionary(e => e.Entity.Id.Value, e => e.Entity.TenantId.Value);

        foreach (var entry in ChangeTracker.Entries<StepResult>())
        {
            if (entry.State != EntityState.Added) continue;
            // Found live via the frontend build (a real POST /runs against
            // FlowEngine.Execute, never exercised end-to-end since this
            // shadow property was added): the shadow FK's CLR type matches
            // the owning FlowRun.Id's value-converter type (FlowRunId), not
            // the raw Guid underneath it — casting straight to Guid threw
            // InvalidCastException on every single step ever executed.
            var flowRunId = ((FlowRunId)entry.Property("FlowRunId").CurrentValue!).Value;
            if (flowRunTenantsById.TryGetValue(flowRunId, out var tenantId))
                entry.Property("TenantId").CurrentValue = tenantId;
        }
    }

    public DbSet<Flow> Flows => Set<Flow>();
    public DbSet<ApiCollection> ApiCollections => Set<ApiCollection>();
    public DbSet<FlowRun> FlowRuns => Set<FlowRun>();
    public DbSet<ModuleRegistration> ModuleRegistrations => Set<ModuleRegistration>();
    public DbSet<Yukti.Domain.IdentityAccess.User> Users => Set<Yukti.Domain.IdentityAccess.User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<FlowReportReadModel> FlowReports => Set<FlowReportReadModel>();
    public DbSet<TrendAggregateReadModel> TrendAggregates => Set<TrendAggregateReadModel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new RoleConfiguration());
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new ModuleRegistrationConfiguration());
        modelBuilder.ApplyConfiguration(new FlowConfiguration());
        modelBuilder.ApplyConfiguration(new ApiCollectionConfiguration());
        modelBuilder.ApplyConfiguration(new FlowRunConfiguration());
        modelBuilder.ApplyConfiguration(new AuditEntryConfiguration());

        // FR-API-02's dedup table — not a domain aggregate, configured
        // inline rather than via its own IEntityTypeConfiguration file.
        modelBuilder.Entity<IdempotencyRecord>(e =>
        {
            e.ToTable("idempotency_keys");
            e.HasKey(r => new { r.TenantId, r.Key });
        });

        // FR-EVT-01's Tier 2 outbox — see OutboxMessage's own doc comment.
        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox_messages");
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).HasConversion(id => id.Value, v => new OutboxMessageId(v));
            e.Property(m => m.EventTypeName).IsRequired();
            e.Property(m => m.PayloadJson).HasColumnType("jsonb").IsRequired();
            e.Property(m => m.OccurredAt).IsRequired();
            e.Property(m => m.ProcessedAt);
            // OutboxRelayHostedService's hot query — unprocessed rows, oldest first.
            e.HasIndex(m => m.ProcessedAt);
        });

        // FR-CQRS-02's read model — see FlowReportReadModel's own doc comment.
        modelBuilder.Entity<FlowReportReadModel>(e =>
        {
            e.ToTable("flow_reports");
            e.HasKey(r => r.FlowRunId);
            e.Property(r => r.FlowRunId).HasConversion(id => id.Value, v => new FlowRunId(v));
            e.Property(r => r.FlowId).HasConversion(id => id.Value, v => new FlowId(v));
            e.Property(r => r.TenantId).HasConversion(id => id.Value, v => new TenantId(v));
            e.Property(r => r.FinalStatus).HasConversion<string>();
            e.HasIndex(r => r.TenantId); // FR-DB-03: tenant_id leads (alone here — only column besides the PK worth indexing on)
        });

        // FR-CQRS-03's batch-recomputed read model — one row per tenant, TenantId is its own PK.
        modelBuilder.Entity<TrendAggregateReadModel>(e =>
        {
            e.ToTable("trend_aggregates");
            e.HasKey(r => r.TenantId);
            e.Property(r => r.TenantId).HasConversion(id => id.Value, v => new TenantId(v));
        });
    }
}
