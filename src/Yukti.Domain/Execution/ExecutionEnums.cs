namespace Yukti.Domain.Execution;

// Closed enums, unlike ModuleKind — the set of possible outcomes for a step
// or run is a fixed, core piece of the orchestration model that no plugin
// should ever be able to extend (Architecture Principle 20.3's uniform
// reporting requirement). (Volume 1 Part II §11.3)

public enum StepStatus { Passed, Failed, Skipped }

public enum RunStatus { Pending, Running, Passed, Failed, Cancelled }

public enum RunTrigger { Api, Scheduled, Webhook }

/// <summary>
/// Direct, concrete implementation of FR-AI-3's requirement that every
/// AI-assisted intervention be recorded distinguishably. Its presence (or
/// deliberate absence) on a StepResult is itself meaningful — the type
/// system guarantees a caller checking `StepResult.AiAttribution is not null`
/// reliably detects AI involvement. (Volume 1 Part II §11.5)
/// </summary>
public sealed record AiAttribution
{
    public required string Capability { get; init; } // "selector-heal" | "flow-generation" | "log-triage" | "analyst" | "coder"
    public required double Confidence { get; init; }
    public string? OriginalValue { get; init; }
    public string? SuggestedValue { get; init; }
    public bool WasAccepted { get; init; }
    public required string ModelIdentifier { get; init; }

    // D5-T6: CLAUDE.md principle 3 names this as one of the four required fields
    // ("capability, confidence, model, prompt version") — missing until now because nothing in
    // this codebase had ever actually constructed an AiAttribution (confirmed: no `new
    // AiAttribution` anywhere before D5-T6), so the gap went unnoticed until Sarvagna's planning
    // pipeline (D5-T3) became the first real producer.
    public required string PromptVersion { get; init; }
}
