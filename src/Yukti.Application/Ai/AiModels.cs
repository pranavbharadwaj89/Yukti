namespace Yukti.Application.Ai;

/// <summary>
/// Volume 3 — AI Architecture: Request DTO for LLM-assisted test assertion generation.
/// </summary>
public sealed record GenerateAssertionsRequest(
    string Method,
    string Url,
    int StatusCode,
    string? ResponseBodyJson,
    IReadOnlyDictionary<string, string>? ResponseHeaders = null);

/// <summary>
/// A single generated assertion rule with confidence and explanation.
/// </summary>
public sealed record GeneratedAssertion(
    string Type,
    string Expression,
    string ExpectedValue,
    string Description,
    double Confidence);

/// <summary>
/// Result of assertion generation containing suggested assertions and model provenance.
/// </summary>
public sealed record GenerateAssertionsResult(
    IReadOnlyList<GeneratedAssertion> Assertions,
    string ModelUsed,
    double Confidence,
    string? Reasoning);

/// <summary>
/// Volume 3 Section 10 — Request for Self-Healing selector auto-repair.
/// </summary>
public sealed record HealSelectorRequest(
    string FailedSelector,
    string? TargetTag,
    string? DomSnippet,
    string? ErrorMessage,
    string? PageUrl);

/// <summary>
/// Repaired selector proposal with confidence score and human explanation.
/// </summary>
public sealed record HealSelectorResult(
    string OriginalSelector,
    string HealedSelector,
    string Strategy,
    double Confidence,
    string Explanation,
    string ModelUsed);

/// <summary>
/// Volume 3 Section 2 & 8 — Request for AI-powered flow suggestion from natural language.
/// </summary>
public sealed record SuggestFlowRequest(
    string NaturalLanguageDescription,
    string? TargetBaseUrl,
    IReadOnlyList<string>? PreferredModules = null);

/// <summary>
/// A step suggested for the flow authoring canvas.
/// </summary>
public sealed record SuggestedFlowStep(
    string Module,
    string Action,
    IReadOnlyDictionary<string, object> Parameters,
    string Description);

/// <summary>
/// Proposed complete multi-module flow.
/// </summary>
public sealed record SuggestFlowResult(
    string FlowName,
    string Description,
    IReadOnlyList<SuggestedFlowStep> Steps,
    double Confidence,
    string ModelUsed);
