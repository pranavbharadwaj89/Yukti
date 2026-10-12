using Yukti.Domain.ModulePlugin;

namespace Yukti.Domain.FlowAuthoring;

/// <summary>
/// Per strict DDD discipline, aggregates never directly reference or query
/// one another. Flow.Publish needs to know whether every referenced module
/// action currently exists in some ModuleRegistration (a different
/// aggregate) — this port is what the calling application service satisfies
/// via a repository query, letting the invariant be enforced at publish
/// time without Flow depending on ModuleRegistration's repository directly
/// (Volume 1 Part II §9.5). Async — the previous synchronous shape forced
/// both implementations to block the calling thread on the repository's
/// own async query, a sync-over-async pattern SUTRADHAR.md §7 bans (D2-T5
/// found and fixed this alongside the required-parameter check).
/// </summary>
public interface IModuleActionResolver
{
    /// <summary>Null when the action isn't registered. Existence and full
    /// schema (for the required-parameter check, D2-T5) were two separate
    /// members until today; consolidated to one, since nothing needs
    /// existence without also needing the schema to validate against.</summary>
    Task<ModulePlugin.ActionSchema?> TryGetActionSchema(ModuleKind module, string action, CancellationToken ct);
}
