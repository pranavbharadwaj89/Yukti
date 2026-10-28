using System.Collections.Concurrent;
using Yukti.Application.Abstractions;
using Yukti.Domain.ApiTesting;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Domain.UiVocabulary;

namespace Yukti.Infrastructure.InMemory;

public sealed class InMemoryRequestTemplateRepository : IRequestTemplateRepository
{
    private readonly ConcurrentDictionary<Guid, RequestTemplate> _store = new();
    private readonly InMemoryUnitOfWorkFactory _uowFactory;

    public InMemoryRequestTemplateRepository(InMemoryUnitOfWorkFactory uowFactory) => _uowFactory = uowFactory;

    public Task<RequestTemplate?> GetById(RequestTemplateId id, CancellationToken ct) =>
        Task.FromResult(_store.TryGetValue(id.Value, out var template) ? template : null);

    public Task Save(RequestTemplate template, CancellationToken ct)
    {
        _uowFactory.StageSave(() => _store[template.Id.Value] = template, template);
        return Task.CompletedTask;
    }

    public Task Delete(RequestTemplate template, CancellationToken ct)
    {
        _uowFactory.StageSave(() => _store.TryRemove(template.Id.Value, out _), template);
        return Task.CompletedTask;
    }
}

public sealed class InMemoryObjectRepositoryRepository : IObjectRepositoryRepository
{
    private readonly ConcurrentDictionary<Guid, ObjectRepository> _store = new();
    private readonly InMemoryUnitOfWorkFactory _uowFactory;

    public InMemoryObjectRepositoryRepository(InMemoryUnitOfWorkFactory uowFactory) => _uowFactory = uowFactory;

    public Task<ObjectRepository?> GetById(RepositoryId id, CancellationToken ct) =>
        Task.FromResult(_store.TryGetValue(id.Value, out var repository) ? repository : null);

    public Task Save(ObjectRepository repository, CancellationToken ct)
    {
        _uowFactory.StageSave(() => _store[repository.Id.Value] = repository, repository);
        return Task.CompletedTask;
    }

    public Task Delete(ObjectRepository repository, CancellationToken ct)
    {
        _uowFactory.StageSave(() => _store.TryRemove(repository.Id.Value, out _), repository);
        return Task.CompletedTask;
    }

    /// <summary>Test-only accessor for IObjectRepositoryElementQuery's in-memory counterpart --
    /// searching every repository for a tenant is a Query concern (this repository itself
    /// deliberately has no Find/search method, matching every other repository here), but the
    /// in-memory query needs *some* way to see what's been saved without a second, duplicate
    /// store.</summary>
    internal IEnumerable<ObjectRepository> AllSaved => _store.Values;
}

public sealed class InMemoryObjectRepositoryElementQuery : IObjectRepositoryElementQuery
{
    private readonly InMemoryObjectRepositoryRepository _repositories;

    public InMemoryObjectRepositoryElementQuery(InMemoryObjectRepositoryRepository repositories) => _repositories = repositories;

    public Task<Locator?> FindElement(string page, string element, TenantId tenantId, CancellationToken ct) =>
        Task.FromResult(_repositories.AllSaved
            .Where(r => r.TenantId == tenantId)
            .Select(r => r.FindElement(page, element))
            .FirstOrDefault(l => l is not null));
}

public sealed class InMemoryFlowRepository : IFlowRepository
{
    private readonly ConcurrentDictionary<Guid, Flow> _store = new();
    private readonly InMemoryUnitOfWorkFactory _uowFactory;

    public InMemoryFlowRepository(InMemoryUnitOfWorkFactory uowFactory) => _uowFactory = uowFactory;

    public Task<Flow?> GetById(FlowId id, CancellationToken ct) =>
        Task.FromResult(_store.TryGetValue(id.Value, out var flow) ? flow : null);

    public Task<Flow?> GetLatestVersionByFamily(FlowFamilyId familyId, CancellationToken ct) =>
        Task.FromResult(_store.Values.Where(f => f.FamilyId == familyId).OrderByDescending(f => f.Version).FirstOrDefault());

    public Task Save(Flow flow, CancellationToken ct)
    {
        _uowFactory.StageSave(() => _store[flow.Id.Value] = flow, flow);
        return Task.CompletedTask;
    }
}

public sealed class InMemoryFlowRunRepository : IFlowRunRepository
{
    private readonly ConcurrentDictionary<Guid, FlowRun> _store = new();
    private readonly InMemoryUnitOfWorkFactory _uowFactory;

    public InMemoryFlowRunRepository(InMemoryUnitOfWorkFactory uowFactory) => _uowFactory = uowFactory;

    public Task<FlowRun?> GetById(FlowRunId id, CancellationToken ct) =>
        Task.FromResult(_store.TryGetValue(id.Value, out var run) ? run : null);

    public Task Save(FlowRun run, CancellationToken ct)
    {
        _uowFactory.StageSave(() => _store[run.Id.Value] = run, run);
        return Task.CompletedTask;
    }
}

public sealed class InMemoryModuleRegistrationRepository : IModuleRegistrationRepository
{
    private readonly ConcurrentDictionary<Guid, ModuleRegistration> _store = new();
    private readonly InMemoryUnitOfWorkFactory _uowFactory;

    public InMemoryModuleRegistrationRepository(InMemoryUnitOfWorkFactory uowFactory) => _uowFactory = uowFactory;

    public Task<ModuleRegistration?> GetById(ModuleRegistrationId id, CancellationToken ct) =>
        Task.FromResult(_store.TryGetValue(id.Value, out var reg) ? reg : null);

    public Task<ModuleRegistration?> GetByKind(ModuleKind kind, TenantId? tenantId, CancellationToken ct) =>
        Task.FromResult(_store.Values.FirstOrDefault(r => r.Kind.Value == kind.Value && r.TenantId == tenantId && r.IsActive));

    public Task Save(ModuleRegistration registration, CancellationToken ct)
    {
        _uowFactory.StageSave(() => _store[registration.Id.Value] = registration, registration);
        return Task.CompletedTask;
    }
}

public sealed class InMemoryUserRepository : IUserRepository
{
    private readonly ConcurrentDictionary<Guid, User> _store = new();
    private readonly InMemoryUnitOfWorkFactory _uowFactory;

    public InMemoryUserRepository(InMemoryUnitOfWorkFactory uowFactory) => _uowFactory = uowFactory;

    public Task<User?> GetById(UserId id, CancellationToken ct) =>
        Task.FromResult(_store.TryGetValue(id.Value, out var user) ? user : null);

    public Task<User?> GetByEmail(string email, CancellationToken ct) =>
        Task.FromResult(_store.Values.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase)));

    public Task Save(User user, CancellationToken ct)
    {
        _uowFactory.StageSave(() => _store[user.Id.Value] = user, user);
        return Task.CompletedTask;
    }
}

public sealed class InMemoryRoleRepository : IRoleRepository
{
    private readonly ConcurrentDictionary<Guid, Role> _store = new();
    private readonly InMemoryUnitOfWorkFactory _uowFactory;

    public InMemoryRoleRepository(InMemoryUnitOfWorkFactory uowFactory) => _uowFactory = uowFactory;

    public Task<Role?> GetById(RoleId id, CancellationToken ct) =>
        Task.FromResult(_store.TryGetValue(id.Value, out var role) ? role : null);

    public Task<IReadOnlyList<Role>> GetByIds(IReadOnlyList<RoleId> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Role>>(ids
            .Select(id => _store.TryGetValue(id.Value, out var role) ? role : null)
            .Where(r => r is not null)
            .Cast<Role>()
            .ToList());

    public Task<Role?> GetByName(string name, TenantId? tenantId, CancellationToken ct) =>
        Task.FromResult(_store.Values.FirstOrDefault(r =>
            r.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && r.TenantId == tenantId));

    public Task Save(Role role, CancellationToken ct)
    {
        _uowFactory.StageSave(() => _store[role.Id.Value] = role, role);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Backs Flow.Publish's cross-aggregate check (Volume 1 Part II §9.5) by
/// querying the module registration repository — the pattern every real
/// Infrastructure implementation follows, demo-grade or not.
/// </summary>
public sealed class ModuleActionResolver : IModuleActionResolver
{
    private readonly IModuleRegistrationRepository _registrations;
    private readonly TenantId? _tenantId;

    public ModuleActionResolver(IModuleRegistrationRepository registrations, TenantId? tenantId = null)
    {
        _registrations = registrations;
        _tenantId = tenantId;
    }

    public async Task<ActionSchema?> TryGetActionSchema(ModuleKind module, string action, CancellationToken ct)
    {
        var registration = await ResolveRegistration(module, ct);
        return registration?.Actions.FirstOrDefault(a => a.ActionName == action && !a.IsDeprecated)?.Schema;
    }

    private async Task<ModuleRegistration?> ResolveRegistration(ModuleKind module, CancellationToken ct)
    {
        var registration = await _registrations.GetByKind(module, null, ct);
        if (registration is not null || _tenantId is null)
            return registration;
        return await _registrations.GetByKind(module, _tenantId, ct);
    }
}
