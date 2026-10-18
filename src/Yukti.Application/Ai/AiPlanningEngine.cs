using System.Text.Json.Nodes;

namespace Yukti.Application.Ai;

/// <summary>
/// D5-T3: the single entry point every Sarvagna capability (Analyst,
/// Coder — D5-T4/T5) calls through. Name kept from the blueprint
/// (<c>docs/specification/volume-3-ai-architecture/README.md</c> §2.3-2.4,
/// <c>AiPlanningEngine</c>/<c>plan&lt;TOutput&gt;</c>) per SUTRADHAR §2.8 —
/// record naming, don't invent a new one silently.
///
/// Deliberately a smaller seven-stages-on-paper, four-in-code engine than
/// the blueprint's: Day 5's own non-goals rule out RAG/Memory retrieval
/// ("Context, kept deliberately small" — the module registry only) and a
/// separate tenant-policy stage (no per-tenant AI settings until v0.2). The
/// stages that remain are exactly D5-T3's own list: guardrail pre-check →
/// render the versioned prompt → call the provider → validate the output
/// (one retry) → record cost → return. "Context" is the caller's job, not
/// this engine's — Analyst/Coder know what registry subset belongs in
/// their own prompt; this engine only renders what it's handed into the
/// template, the same way for every capability.
/// </summary>
public interface IAiPlanningEngine
{
    Task<AiPlanningResult<TOutput>> PlanAsync<TOutput>(AiPlanningRequest<TOutput> request, CancellationToken ct);
}

/// <summary>
/// <paramref name="Validate"/> is the capability's own business validation —
/// deliberately not a generic JSON-Schema check (no such library is in this
/// solution, and D5-T5's own spec is explicit: Coder's output goes through
/// "Day 2's publish validator, not a second copy," which is exactly this
/// delegate, not a schema file). Async (not just a <c>Func&lt;JsonNode?,
/// AiValidationOutcome&lt;TOutput&gt;&gt;</c>) because Coder's own validator
/// calls the real, async <c>Flow.Publish</c> — D5-T3 itself never needed
/// this (Analyst's validator is pure JSON-shape checking), but widening it
/// here is the honest fix per CLAUDE.md §7 ("async all the way... no
/// <c>.Result</c>/<c>.Wait()</c>"), not a second, blocking copy. Returns the
/// typed output plus the error list the retry prompt gets built from when
/// it's non-empty.
///
/// <see cref="MaxTokens"/> has no default here deliberately — the output-token ceiling comes
/// from <see cref="IAiCapabilityOptionsProvider"/> (token-economy spec §R4: configuration, never
/// a literal spread through callers), so <see cref="AiPlanningEngine"/> sources it per
/// <see cref="Capability"/> rather than accepting whatever a caller happens to pass.
/// </summary>
public sealed record AiPlanningRequest<TOutput>(
    string Capability,
    string PromptVersion,
    string Description,
    string PromptContext,
    Func<JsonNode?, CancellationToken, Task<AiValidationOutcome<TOutput>>> Validate,
    TimeSpan Timeout = default);

public sealed record AiValidationOutcome<TOutput>(bool IsValid, TOutput? Output, IReadOnlyList<string> Errors)
{
    public static AiValidationOutcome<TOutput> Valid(TOutput output) => new(true, output, Array.Empty<string>());
    public static AiValidationOutcome<TOutput> Invalid(IReadOnlyList<string> errors) => new(false, default, errors);
}

/// <summary>
/// <see cref="Attempt"/>/<see cref="Confidence"/> (D5-T6) are computed by the engine itself, once,
/// from the same loop that already knows which attempt succeeded — so every capability gets a
/// consistent, formula-based confidence (<see cref="AiConfidence.FromAttempt"/>) for free, never a
/// per-capability literal.
/// </summary>
public sealed record AiPlanningResult<TOutput>(
    TOutput Output,
    string Capability,
    string PromptVersion,
    string Provider,
    int InputTokens,
    int OutputTokens,
    TimeSpan Latency,
    IReadOnlyList<string> GuardrailChecks,
    bool UsedRetry,
    int Attempt,
    double Confidence);

/// <summary>
/// Raised when the provider returned something, twice, and neither attempt
/// passed <see cref="AiPlanningRequest{TOutput}.Validate"/> — the "no
/// partial flow, no heuristic substitute" failure D5-T5 names explicitly.
/// Distinct from <see cref="AiUnavailableException"/> (the provider itself
/// failed) and <see cref="AiDisabledException"/> (AI is off) — three
/// different reasons a caller needs to tell apart, three different types.
/// </summary>
public sealed class AiGenerationFailedException : Exception
{
    public IReadOnlyList<string> ValidationErrors { get; }

    public AiGenerationFailedException(IReadOnlyList<string> validationErrors)
        : base("AI generation failed validation after a retry: " + string.Join("; ", validationErrors))
    {
        ValidationErrors = validationErrors;
    }
}

/// <summary>
/// D5-T3's guardrail pre-check, run before anything else: an empty
/// description costs nothing to reject, so it's rejected before the
/// provider is ever involved.
/// </summary>
public sealed class AiGuardrailRejectedException : Exception
{
    public AiGuardrailRejectedException(string reason) : base(reason) { }
}

/// <summary>
/// D5-T9 (not yet built) is the real, persisted implementation
/// (<c>ai_cost_records</c>). D5-T3 only needs somewhere to hand the data —
/// defining the seam now means D5-T9 is a new implementation of this one
/// interface, not a change to the planning engine itself.
/// </summary>
public interface IAiCostRecorder
{
    Task RecordAsync(AiCostRecordData record, CancellationToken ct);
}

/// <summary>
/// One row per provider call, not per <see cref="IAiPlanningEngine.PlanAsync{TOutput}"/> invocation
/// (token-economy spec §R5): a validation failure that triggers a retry still spent real tokens on
/// its own attempt, so it gets its own row — <see cref="Attempt"/> (1-based) plus <see cref="Succeeded"/>
/// is how two rows from the same call are told apart. A guardrail reject (empty input, over-length,
/// no modules registered, assembled prompt over the capability's cap) never reaches the provider, so
/// it never reaches this type at all — nothing was spent, nothing to record (§R1).
/// </summary>
public sealed record AiCostRecordData(
    string Capability,
    string Provider,
    string PromptVersion,
    int Attempt,
    bool Succeeded,
    int InputTokens,
    int OutputTokens,
    TimeSpan Latency,
    DateTimeOffset OccurredAt);

/// <summary>
/// D5-T3: "Prompts live in Sarvagna/Prompts/&lt;capability&gt;.v1.md,
/// loaded by name and version. Changing a prompt means a new file, never
/// an edit in place." This port is the "loaded by name and version" half —
/// the file-system path is an Infrastructure concern (<see
/// cref="Yukti.Infrastructure.Ai.FilePromptLoader"/>), not this engine's.
/// </summary>
public interface IPromptLoader
{
    Task<string> LoadAsync(string capability, string version, CancellationToken ct);
}

/// <summary>Raised when a prompt file for the requested capability/version doesn't exist — a build-time mistake, not a runtime AI failure.</summary>
public sealed class PromptNotFoundException : Exception
{
    public PromptNotFoundException(string capability, string version)
        : base($"No prompt file found for capability '{capability}' version '{version}'.") { }
}
