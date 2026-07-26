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

public interface IRoleRepository
{
    Task<Yukti.Domain.IdentityAccess.Role?> GetById(RoleId id, CancellationToken ct);
    Task<Yukti.Domain.IdentityAccess.Role?> GetByName(string name, TenantId? tenantId, CancellationToken ct);
    Task Save(Yukti.Domain.IdentityAccess.Role role, CancellationToken ct);
}
