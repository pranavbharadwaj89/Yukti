using Microsoft.EntityFrameworkCore;
using Yukti.Application.Abstractions;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;

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
