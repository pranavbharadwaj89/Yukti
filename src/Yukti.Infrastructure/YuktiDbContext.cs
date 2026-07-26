using Microsoft.EntityFrameworkCore;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.ModulePlugin;
using Yukti.Infrastructure.Configurations;

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

    public DbSet<Flow> Flows => Set<Flow>();
    public DbSet<FlowRun> FlowRuns => Set<FlowRun>();
    public DbSet<ModuleRegistration> ModuleRegistrations => Set<ModuleRegistration>();
    public DbSet<Yukti.Domain.IdentityAccess.User> Users => Set<Yukti.Domain.IdentityAccess.User>();
    public DbSet<Role> Roles => Set<Role>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new RoleConfiguration());
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new ModuleRegistrationConfiguration());
        modelBuilder.ApplyConfiguration(new FlowConfiguration());
        modelBuilder.ApplyConfiguration(new FlowRunConfiguration());
    }
}
