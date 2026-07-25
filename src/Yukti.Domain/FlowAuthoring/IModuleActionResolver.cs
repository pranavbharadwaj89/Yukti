using Yukti.Domain.ModulePlugin;

namespace Yukti.Domain.FlowAuthoring;

/// <summary>
/// Per strict DDD discipline, aggregates never directly reference or query
/// one another. Flow.Publish needs to know whether every referenced module
/// action currently exists in some ModuleRegistration (a different
/// aggregate) — this port is what the calling application service satisfies
/// via a repository query, letting the invariant be enforced synchronously
/// at publish time without Flow depending on ModuleRegistration's repository
/// directly. (Volume 1 Part II §9.5)
/// </summary>
public interface IModuleActionResolver
{
    bool ActionExists(ModuleKind module, string action);
}
