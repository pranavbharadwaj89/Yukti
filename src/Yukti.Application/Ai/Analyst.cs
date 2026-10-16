namespace Yukti.Application.Ai;

/// <summary>
/// D5-T4: Analyst's output — English description + the registered module registry → this.
/// <see cref="CandidateModuleKinds"/> is the module(s) the request genuinely needs (a flow can
/// legitimately span several, e.g. web + api + logs for a UC-1-shaped request — multiple
/// non-ambiguous candidates is the normal multi-module case, not itself an ambiguity).
/// <see cref="Ambiguities"/> is how Analyst refuses to guess: an out-of-scope request (no
/// registered module fits) and a genuinely two-module-ambiguous request both produce a named
/// entry here, never a silent, possibly-wrong pick. The one thing <see cref="IAnalyst"/>'s own
/// validator enforces structurally is that these two lists are never *both* empty — Analyst must
/// either commit to module(s) or name why it can't, never return nothing at all.
/// </summary>
public sealed record ParsedIntent(
    IReadOnlyList<string> CandidateModuleKinds,
    IReadOnlyList<string> Assertions,
    IReadOnlyList<string> Ambiguities);

/// <summary>
/// D5-T4: the first Sarvagna capability built on <see cref="IAiPlanningEngine"/> (D5-T3). The
/// concrete implementation (<c>Yukti.Infrastructure.Ai.Analyst</c>) needs the real module
/// registry (<c>Yukti.Orchestration.IModuleRegistry</c>) to build its own minimal context and to
/// validate that <see cref="ParsedIntent.CandidateModuleKinds"/> never names a module that isn't
/// actually registered — that's an Infrastructure-layer dependency (Orchestration), so this port
/// stays here, deliberately free of it, the same separation <see cref="IAiProvider"/> and
/// <see cref="IAiPlanningEngine"/> already keep between Application and Infrastructure.
/// </summary>
public interface IAnalyst
{
    Task<ParsedIntent> AnalyzeAsync(string description, CancellationToken ct);
}
