using Yukti.Domain.UiVocabulary;

namespace Yukti.Domain.Execution;

/// <summary>
/// Web UI Testing Module Spec, WEB-T3: the module gate's own design — "a step that succeeds via
/// a fallback passes and is flagged, with the report saying which fallback was used and why the
/// primary failed." Present (or deliberately absent) on a StepResult exactly like AiAttribution —
/// its presence alone means a fallback was used; a step whose primary locator resolved directly
/// carries no LocatorResolutionInfo at all, never one with UsedFallback false.
/// </summary>
public sealed record LocatorResolutionInfo(
    LocatorStrategy StrategyUsed,
    string PrimaryFailureReason,
    string? Description);
