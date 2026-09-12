namespace Yukti.Application.Ai;

/// <summary>
/// Volume 3 — AI Architecture: Primary interface for AI capability engines
/// (authoring acceleration, maintenance reduction, and diagnostic acceleration).
/// Adheres strictly to the permanent boundaries: proposals only (never autonomous mutation),
/// and deterministic fallback support when no LLM provider is active.
/// </summary>
public interface IAiAssistantService
{
    Task<GenerateAssertionsResult> GenerateAssertionsAsync(GenerateAssertionsRequest request, CancellationToken ct = default);
    Task<HealSelectorResult> HealSelectorAsync(HealSelectorRequest request, CancellationToken ct = default);
    Task<SuggestFlowResult> SuggestFlowAsync(SuggestFlowRequest request, CancellationToken ct = default);
}
