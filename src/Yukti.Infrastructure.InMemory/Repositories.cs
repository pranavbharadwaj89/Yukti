using System.Collections.Concurrent;
using Yukti.Application.Abstractions;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure.InMemory;

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

    public bool ActionExists(ModuleKind module, string action)
    {
        var registration = _registrations.GetByKind(module, null, CancellationToken.None).GetAwaiter().GetResult()
            ?? (_tenantId is not null
                ? _registrations.GetByKind(module, _tenantId, CancellationToken.None).GetAwaiter().GetResult()
                : null);
        return registration is not null && registration.HasAction(action);
    }
}
