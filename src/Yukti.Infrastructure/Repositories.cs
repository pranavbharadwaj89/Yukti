using Microsoft.EntityFrameworkCore;
using Yukti.Application.Abstractions;
using Yukti.Domain.ApiTesting;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.Auditing;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.ProjectManagement;
using Yukti.Domain.Scheduling;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.ReadModels;

namespace Yukti.Infrastructure;

// Every repository follows the same shape: GetXxx queries are plain LINQ
// (owned collections — FlowSteps, StepResults, RetryAttempts,
// ModuleActionEntries — are always eagerly loaded by EF Core for owned
// types, no explicit .Include needed). Save() only needs to Add() a
// never-before-tracked aggregate; an aggregate loaded earlier in the same
// DbContext scope and mutated in-place via its own domain methods is
// already being watched by EF Core's change tracker, so Save() on it is a
// deliberate no-op — the actual write happens at EfUnitOfWork.Commit().
//
// FR-TENANT-01 Layer 1: GetById on every tenant-scoped aggregate (Flow,
// FlowRun, User) filters by ITenantContextAccessor.CurrentTenantId at the
// query itself (FR-REPO-06), not a post-fetch check. A null CurrentTenantId
// (no authenticated request, or process-startup seeding) matches nothing
// for these — fail closed, never fail open. GetByEmail/GetByName/GetByKind
// keep their existing explicit-tenantId-parameter designs unchanged: they
// were never the actual gap (a caller already has to know/supply the
// tenant); GetById(id) was, since any authenticated caller could
// previously fetch any row by guessing its Guid with zero scoping at all.

public sealed class EfFlowRepository : IFlowRepository
{
    private readonly YuktiDbContext _context;
    private readonly ITenantContextAccessor _tenant;

    public EfFlowRepository(YuktiDbContext context, ITenantContextAccessor tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public Task<Flow?> GetById(FlowId id, CancellationToken ct) =>
        _context.Flows.FirstOrDefaultAsync(f => f.Id == id && f.TenantId == _tenant.CurrentTenantId, ct);

    public Task<Flow?> GetLatestVersionByFamily(FlowFamilyId familyId, CancellationToken ct) =>
        _context.Flows.Where(f => f.FamilyId == familyId && f.TenantId == _tenant.CurrentTenantId)
            .OrderByDescending(f => f.Version).FirstOrDefaultAsync(ct);

    public async Task Save(Flow flow, CancellationToken ct)
    {
        if (_context.Entry(flow).State == EntityState.Detached)
            await _context.AddAsync(flow, ct);
    }
}

public sealed class EfFlowRunRepository : IFlowRunRepository
{
    private readonly YuktiDbContext _context;
    private readonly ITenantContextAccessor _tenant;

    public EfFlowRunRepository(YuktiDbContext context, ITenantContextAccessor tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public Task<FlowRun?> GetById(FlowRunId id, CancellationToken ct) =>
        _context.FlowRuns.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == _tenant.CurrentTenantId, ct);

    public async Task Save(FlowRun run, CancellationToken ct)
    {
        if (_context.Entry(run).State == EntityState.Detached)
            await _context.AddAsync(run, ct);
    }
}

public sealed class EfModuleRegistrationRepository : IModuleRegistrationRepository
{
    private readonly YuktiDbContext _context;
    private readonly ITenantContextAccessor _tenant;

    public EfModuleRegistrationRepository(YuktiDbContext context, ITenantContextAccessor tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    // Global (BuiltIn/Verified, TenantId null) modules are visible to every
    // tenant; Community-tier modules are visible only to the tenant that
    // installed them.
    public Task<ModuleRegistration?> GetById(ModuleRegistrationId id, CancellationToken ct) =>
        _context.ModuleRegistrations.FirstOrDefaultAsync(
            m => m.Id == id && (m.TenantId == null || m.TenantId == _tenant.CurrentTenantId), ct);

    public Task<ModuleRegistration?> GetByKind(ModuleKind kind, TenantId? tenantId, CancellationToken ct) =>
        _context.ModuleRegistrations.FirstOrDefaultAsync(
            m => m.Kind == kind && m.TenantId == tenantId && m.IsActive, ct);

    public async Task Save(ModuleRegistration registration, CancellationToken ct)
    {
        if (_context.Entry(registration).State == EntityState.Detached)
            await _context.AddAsync(registration, ct);
    }
}

public sealed class EfApiCollectionRepository : IApiCollectionRepository
{
    private readonly YuktiDbContext _context;
    private readonly ITenantContextAccessor _tenant;

    public EfApiCollectionRepository(YuktiDbContext context, ITenantContextAccessor tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public Task<ApiCollection?> GetById(ApiCollectionId id, CancellationToken ct) =>
        _context.ApiCollections.FirstOrDefaultAsync(c => c.Id == id && c.TenantId == _tenant.CurrentTenantId, ct);

    public async Task Save(ApiCollection collection, CancellationToken ct)
    {
        if (_context.Entry(collection).State == EntityState.Detached)
            await _context.AddAsync(collection, ct);
    }

    public Task Delete(ApiCollection collection, CancellationToken ct)
    {
        _context.Remove(collection);
        return Task.CompletedTask;
    }
}

public sealed class EfProjectRepository : IProjectRepository
{
    private readonly YuktiDbContext _context;
    private readonly ITenantContextAccessor _tenant;

    public EfProjectRepository(YuktiDbContext context, ITenantContextAccessor tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public Task<Project?> GetById(ProjectId id, CancellationToken ct) =>
        _context.Projects.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == _tenant.CurrentTenantId, ct);

    public async Task Save(Project project, CancellationToken ct)
    {
        if (_context.Entry(project).State == EntityState.Detached)
            await _context.AddAsync(project, ct);
    }

    public Task Delete(Project project, CancellationToken ct)
    {
        _context.Remove(project);
        return Task.CompletedTask;
    }
}

public sealed class EfTestEnvironmentRepository : ITestEnvironmentRepository
{
    private readonly YuktiDbContext _context;
    private readonly ITenantContextAccessor _tenant;

    public EfTestEnvironmentRepository(YuktiDbContext context, ITenantContextAccessor tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public Task<TestEnvironment?> GetById(TestEnvironmentId id, CancellationToken ct) =>
        _context.TestEnvironments.FirstOrDefaultAsync(e => e.Id == id && e.TenantId == _tenant.CurrentTenantId, ct);

    public async Task Save(TestEnvironment environment, CancellationToken ct)
    {
        if (_context.Entry(environment).State == EntityState.Detached)
            await _context.AddAsync(environment, ct);
    }

    public Task Delete(TestEnvironment environment, CancellationToken ct)
    {
        _context.Remove(environment);
        return Task.CompletedTask;
    }
}

public sealed class EfProjectSummaryQuery : IProjectSummaryQuery
{
    private readonly YuktiDbContext _context;
    public EfProjectSummaryQuery(YuktiDbContext context) => _context = context;

    public async Task<IReadOnlyList<ProjectSummary>> ListByTenant(TenantId tenantId, CancellationToken ct) =>
        await _context.Projects
            .Where(p => p.TenantId == tenantId)
            .Select(p => new ProjectSummary(p.Id, p.Name, p.Description))
            .ToListAsync(ct);
}

public sealed class EfTestEnvironmentSummaryQuery : ITestEnvironmentSummaryQuery
{
    private readonly YuktiDbContext _context;
    public EfTestEnvironmentSummaryQuery(YuktiDbContext context) => _context = context;

    public async Task<IReadOnlyList<TestEnvironmentSummary>> ListByProject(ProjectId projectId, CancellationToken ct) =>
        await _context.TestEnvironments
            .Where(e => e.ProjectId == projectId)
            .Select(e => new TestEnvironmentSummary(e.Id, e.ProjectId, e.Name, e.Variables))
            .ToListAsync(ct);
}

public sealed class EfTriggerRepository : ITriggerRepository
{
    private readonly YuktiDbContext _context;
    private readonly ITenantContextAccessor _tenant;

    public EfTriggerRepository(YuktiDbContext context, ITenantContextAccessor tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public Task<TriggerDefinition?> GetById(TriggerId id, CancellationToken ct) =>
        _context.Triggers.FirstOrDefaultAsync(t => t.Id == id && t.TenantId == _tenant.CurrentTenantId, ct);

    // Deliberately unfiltered by tenant — an incoming webhook HTTP request
    // has no tenant context yet; the trigger it resolves to IS how tenant
    // gets established for that request, same reasoning as EfUserRepository's
    // GetByEmail.
    public Task<TriggerDefinition?> GetByWebhookPath(string webhookPath, CancellationToken ct) =>
        _context.Triggers.FirstOrDefaultAsync(t => t.WebhookPath == webhookPath, ct);

    // Scheduler (Yukti.Worker) runs cross-tenant by design (FR-AUDIT-03's
    // yukti_worker BYPASSRLS role) — every enabled cron trigger across every
    // tenant must be visible here, not scoped to one caller's tenant.
    public async Task<IReadOnlyList<TriggerDefinition>> GetEnabledCronTriggers(CancellationToken ct) =>
        await _context.Triggers.Where(t => t.Kind == TriggerKind.Cron && t.IsEnabled).ToListAsync(ct);

    public async Task Save(TriggerDefinition trigger, CancellationToken ct)
    {
        if (_context.Entry(trigger).State == EntityState.Detached)
            await _context.AddAsync(trigger, ct);
    }
}

public sealed class EfTriggerSummaryQuery : ITriggerSummaryQuery
{
    private readonly YuktiDbContext _context;
    public EfTriggerSummaryQuery(YuktiDbContext context) => _context = context;

    public async Task<IReadOnlyList<TriggerSummary>> ListByTenant(TenantId tenantId, CancellationToken ct) =>
        await _context.Triggers
            .Where(t => t.TenantId == tenantId)
            .Select(t => new TriggerSummary(t.Id, t.FlowId, t.Kind, t.IsEnabled, t.LastFiredAt, t.CronExpression, t.WebhookPath, t.WatchPath))
            .ToListAsync(ct);
}

public sealed class EfAuditSummaryQuery : IAuditSummaryQuery
{
    private readonly YuktiDbContext _context;
    public EfAuditSummaryQuery(YuktiDbContext context) => _context = context;

    public async Task<IReadOnlyList<AuditEntrySummary>> ListByTenant(TenantId tenantId, CancellationToken ct) =>
        await _context.AuditEntries
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.OccurredAt)
            .Select(a => new AuditEntrySummary(a.Id, a.CommandType, a.TenantId, a.Succeeded, a.FailureReason, a.OccurredAt))
            .ToListAsync(ct);

    public async Task<AuditEntryDetail?> GetById(AuditEntryId id, TenantId tenantId, CancellationToken ct)
    {
        var entry = await _context.AuditEntries
            .Where(a => a.Id == id && a.TenantId == tenantId)
            .FirstOrDefaultAsync(ct);
        return entry is null
            ? null
            : new AuditEntryDetail(entry.Id, entry.CommandType, entry.TenantId, entry.Succeeded, entry.FailureReason, entry.Metadata, entry.OccurredAt);
    }
}

public sealed class EfFlowReportSummaryQuery : IFlowReportSummaryQuery
{
    private readonly YuktiDbContext _context;
    public EfFlowReportSummaryQuery(YuktiDbContext context) => _context = context;

    public async Task<IReadOnlyList<FlowReportSummary>> ListByTenant(TenantId tenantId, CancellationToken ct)
    {
        var reports = await _context.FlowReports
            .Where(r => r.TenantId == tenantId)
            .ToListAsync(ct);

        if (reports.Count == 0) return Array.Empty<FlowReportSummary>();

        var flowNames = await _context.Flows
            .Where(f => f.TenantId == tenantId)
            .Select(f => new { f.Id, f.Name })
            .ToDictionaryAsync(f => f.Id, f => f.Name, ct);

        return reports
            .GroupBy(r => r.FlowId)
            .Select(g =>
            {
                var latest = g.OrderByDescending(r => r.OccurredAt).First();
                return new FlowReportSummary(
                    g.Key,
                    flowNames.TryGetValue(g.Key, out var name) ? name : g.Key.Value.ToString(),
                    g.Count(),
                    g.Count(r => r.FinalStatus == RunStatus.Passed),
                    g.Count(r => r.FinalStatus == RunStatus.Failed),
                    latest.OccurredAt,
                    latest.FinalStatus);
            })
            .OrderByDescending(s => s.LastRunAt)
            .ToList();
    }

    public async Task<IReadOnlyList<FlowRunReportEntry>> ListRunsByFlow(FlowId flowId, TenantId tenantId, CancellationToken ct) =>
        await _context.FlowReports
            .Where(r => r.FlowId == flowId && r.TenantId == tenantId)
            .OrderByDescending(r => r.OccurredAt)
            .Select(r => new FlowRunReportEntry(
                r.FlowRunId, r.FinalStatus, r.PassedCount, r.FailedCount, r.SkippedCount,
                r.TotalDuration, r.OccurredAt, r.ProjectedAt))
            .ToListAsync(ct);
}

public sealed class EfUserRepository : IUserRepository
{
    private readonly YuktiDbContext _context;
    private readonly ITenantContextAccessor _tenant;

    public EfUserRepository(YuktiDbContext context, ITenantContextAccessor tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public Task<Yukti.Domain.IdentityAccess.User?> GetById(UserId id, CancellationToken ct) =>
        _context.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == _tenant.CurrentTenantId, ct);

    // Deliberately unfiltered — login and self-registration both need to
    // resolve a user by email before any tenant context exists (that's
    // literally how the caller's tenant gets established). Email has its
    // own unique index across all tenants, so this can never leak more
    // than "an account with this email exists," which login already
    // reveals nothing about beyond the uniform invalid-credentials error.
    public Task<Yukti.Domain.IdentityAccess.User?> GetByEmail(string email, CancellationToken ct) =>
        _context.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

    public async Task Save(Yukti.Domain.IdentityAccess.User user, CancellationToken ct)
    {
        if (_context.Entry(user).State == EntityState.Detached)
            await _context.AddAsync(user, ct);
    }
}

public sealed class EfRoleRepository : IRoleRepository
{
    private readonly YuktiDbContext _context;
    private readonly ITenantContextAccessor _tenant;

    public EfRoleRepository(YuktiDbContext context, ITenantContextAccessor tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    // Baseline roles (Administrator/Flow Author/Flow Runner, TenantId null)
    // are global; custom roles are tenant-scoped. Works correctly even
    // during process-startup seeding (CurrentTenantId null) because the
    // "TenantId == null" branch matches baseline roles regardless.
    public Task<Role?> GetById(RoleId id, CancellationToken ct) =>
        _context.Roles.FirstOrDefaultAsync(
            r => r.Id == id && (r.TenantId == null || r.TenantId == _tenant.CurrentTenantId), ct);

    public async Task<IReadOnlyList<Role>> GetByIds(IReadOnlyList<RoleId> ids, CancellationToken ct) =>
        await _context.Roles
            .Where(r => ids.Contains(r.Id) && (r.TenantId == null || r.TenantId == _tenant.CurrentTenantId))
            .ToListAsync(ct);

    public Task<Role?> GetByName(string name, TenantId? tenantId, CancellationToken ct) =>
        _context.Roles.FirstOrDefaultAsync(r => r.Name == name && r.TenantId == tenantId, ct);

    public async Task Save(Role role, CancellationToken ct)
    {
        if (_context.Entry(role).State == EntityState.Detached)
            await _context.AddAsync(role, ct);
    }
}

/// <summary>Real counterpart to Infrastructure.InMemory's ModuleActionResolver — identical logic, just against a durable repository.</summary>
public sealed class EfModuleActionResolver : IModuleActionResolver
{
    private readonly IModuleRegistrationRepository _registrations;

    public EfModuleActionResolver(IModuleRegistrationRepository registrations) => _registrations = registrations;

    public bool ActionExists(ModuleKind module, string action) =>
        _registrations.GetByKind(module, null, CancellationToken.None).GetAwaiter().GetResult()
            ?.HasAction(action) ?? false;
}
