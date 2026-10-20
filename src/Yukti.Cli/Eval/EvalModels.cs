namespace Yukti.Cli.Eval;

/// <summary>D5-T8: one row of <c>evals/flow_generation/prompts.json</c> — a fixed, hand-written English
/// description plus the scoring expectation Sutradhar recorded when the case was written. Never
/// generated or inferred at eval time; changing what "correct" means for a prompt is a deliberate
/// edit to this file, not something the harness decides for itself.</summary>
public sealed record EvalPromptCase(
    string Id,
    string Description,
    bool ExpectRefusal,
    IReadOnlyList<string> ExpectedModuleKinds,
    bool ExpectAssertions);

/// <summary>
/// One case's outcome. <see cref="Passed"/> is the only field that counts toward the task's own
/// ≥8/10 gate: for a non-refusal case, Coder produced a flow that passed Day 2's own publish
/// validator (Flow.Publish) and Analyst didn't refuse when it shouldn't have; for a refusal case,
/// Analyst correctly refused. <see cref="ModuleCorrect"/>/<see cref="AssertionsCorrect"/> are
/// recorded alongside as diagnostics for prompt-tuning (a flow that fails for the wrong module vs.
/// one that validates but picked the wrong module are different signals) — they never flip
/// <see cref="Passed"/> themselves, since PLAN.md's own gate names only "structural validity."
/// </summary>
public sealed record EvalCaseResult(
    string Id,
    bool Passed,
    bool ActualRefused,
    bool? ModuleCorrect,
    bool? AssertionsCorrect,
    string? Error);

/// <summary>One full run of the harness — stored verbatim as JSON under <c>evals/flow_generation/results/</c>,
/// keyed by <see cref="AnalystPromptVersion"/>/<see cref="CoderPromptVersion"/>/<see cref="RegistryHash"/>
/// so an unchanged version + unchanged registry can be recognized and reused (§R9) without re-running
/// a single case.</summary>
public sealed record EvalRunResult(
    string AnalystPromptVersion,
    string CoderPromptVersion,
    string RegistryHash,
    DateTimeOffset RanAtUtc,
    int PassedCount,
    int TotalCount,
    bool GatePassed,
    IReadOnlyList<EvalCaseResult> Cases);

/// <summary>§R9's own dry-run contract: "prints prompt count × projected tokens × price and exits
/// without calling" — the harness's worst-case cost projection, computed with zero provider calls.</summary>
public sealed record EvalCostEstimate(
    int PromptCount,
    int WorstCaseProviderCalls,
    int WorstCaseInputTokens,
    int WorstCaseOutputTokens,
    decimal WorstCaseCostUsd);
