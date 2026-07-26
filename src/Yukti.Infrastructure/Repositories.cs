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

public sealed class EfFlowRepository : IFlowRepository
{
    private readonly YuktiDbContext _context;
    public EfFlowRepository(YuktiDbContext context) => _context = context;

    public Task<Flow?> GetById(FlowId id, CancellationToken ct) =>
        _context.Flows.FirstOrDefaultAsync(f => f.Id == id, ct);

    public Task<Flow?> GetLatestVersionByFamily(FlowFamilyId familyId, CancellationToken ct) =>
        _context.Flows.Where(f => f.FamilyId == familyId).OrderByDescending(f => f.Version).FirstOrDefaultAsync(ct);

    public async Task Save(Flow flow, CancellationToken ct)
    {
        if (_context.Entry(flow).State == EntityState.Detached)
            await _context.AddAsync(flow, ct);
    }
}

public sealed class EfFlowRunRepository : IFlowRunRepository
{
    private readonly YuktiDbContext _context;
    public EfFlowRunRepository(YuktiDbContext context) => _context = context;

    public Task<FlowRun?> GetById(FlowRunId id, CancellationToken ct) =>
        _context.FlowRuns.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task Save(FlowRun run, CancellationToken ct)
    {
        if (_context.Entry(run).State == EntityState.Detached)
            await _context.AddAsync(run, ct);
    }
}

public sealed class EfModuleRegistrationRepository : IModuleRegistrationRepository
{
    private readonly YuktiDbContext _context;
    public EfModuleRegistrationRepository(YuktiDbContext context) => _context = context;

    public Task<ModuleRegistration?> GetById(ModuleRegistrationId id, CancellationToken ct) =>
        _context.ModuleRegistrations.FirstOrDefaultAsync(m => m.Id == id, ct);

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
    public EfUserRepository(YuktiDbContext context) => _context = context;

    public Task<Yukti.Domain.IdentityAccess.User?> GetById(UserId id, CancellationToken ct) =>
        _context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

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
    public EfRoleRepository(YuktiDbContext context) => _context = context;

    public Task<Role?> GetById(RoleId id, CancellationToken ct) =>
        _context.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);

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
