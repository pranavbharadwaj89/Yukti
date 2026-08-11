using Yukti.Domain.ApiTesting;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;

namespace Yukti.Application.Abstractions;

// One repository per aggregate root, no exceptions. Deliberately absent:
// GetAll(), Find(predicate), pagination — anything resembling a list,
// search, or filter operation is a query (Part II §14.7), never the
// repository's job. (Volume 1 Part III §15.2)

public interface IFlowRepository
{
    Task<Flow?> GetById(FlowId id, CancellationToken ct);
    Task<Flow?> GetLatestVersionByFamily(FlowFamilyId familyId, CancellationToken ct);
    Task Save(Flow flow, CancellationToken ct);
}

public interface IFlowRunRepository
{
    Task<FlowRun?> GetById(FlowRunId id, CancellationToken ct);
    Task Save(FlowRun run, CancellationToken ct);
}

public interface IModuleRegistrationRepository
{
    Task<ModuleRegistration?> GetById(ModuleRegistrationId id, CancellationToken ct);
    Task<ModuleRegistration?> GetByKind(ModuleKind kind, TenantId? tenantId, CancellationToken ct);
    Task Save(ModuleRegistration registration, CancellationToken ct);
}

public interface IUserRepository
{
    Task<Yukti.Domain.IdentityAccess.User?> GetById(UserId id, CancellationToken ct);
    Task<Yukti.Domain.IdentityAccess.User?> GetByEmail(string email, CancellationToken ct);
    Task Save(Yukti.Domain.IdentityAccess.User user, CancellationToken ct);
}

// Deliberate deviation from this file's own "no Delete" convention above:
// Flow is archived, never deleted, because a Flow's execution history
// (FlowRun) references it and must keep resolving. An ApiCollection has no
// such downstream reference — it's disposable, editable scratch content —
// so Delete here is a real repository-shaped operation, not a query.
public interface IApiCollectionRepository
{
    Task<ApiCollection?> GetById(ApiCollectionId id, CancellationToken ct);
    Task Save(ApiCollection collection, CancellationToken ct);
    Task Delete(ApiCollection collection, CancellationToken ct);
}

public interface IRoleRepository
{
    Task<Yukti.Domain.IdentityAccess.Role?> GetById(RoleId id, CancellationToken ct);
    // FR-OPS-03 fallout: PermissionChecker previously fetched a user's roles
    // one round trip at a time in a foreach loop — a genuine N+1, not just
    // a style nit, once round-trip count is the difference between meeting
    // and missing the dispatch-latency budget. One query for every role a
    // user holds, checked in memory afterward.
    Task<IReadOnlyList<Yukti.Domain.IdentityAccess.Role>> GetByIds(IReadOnlyList<RoleId> ids, CancellationToken ct);
    Task<Yukti.Domain.IdentityAccess.Role?> GetByName(string name, TenantId? tenantId, CancellationToken ct);
    Task Save(Yukti.Domain.IdentityAccess.Role role, CancellationToken ct);
}
