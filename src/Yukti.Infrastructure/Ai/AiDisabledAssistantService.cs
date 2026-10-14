using Yukti.Application.Ai;

namespace Yukti.Infrastructure.Ai;

/// <summary>
/// D5-T2: registered as <see cref="IAiAssistantService"/> instead of <see
/// cref="OpenAiAssistantService"/> whenever `Ai:Enabled` is false. Every
/// method raises <see cref="AiDisabledException"/> immediately — no
/// HttpClient, no OpenAI endpoint, no heuristic fallback that would make the
/// response look like it came from somewhere real. "AI is optional" means
/// zero outbound calls are even possible, not just that none happen to fire
/// today, so this class holds no HttpClient and references no provider type
/// at all — there's nothing in it capable of leaving the process.
/// </summary>
public sealed class AiDisabledAssistantService : IAiAssistantService
{
    public Task<GenerateAssertionsResult> GenerateAssertionsAsync(GenerateAssertionsRequest request, CancellationToken ct = default) =>
        throw new AiDisabledException();

    public Task<HealSelectorResult> HealSelectorAsync(HealSelectorRequest request, CancellationToken ct = default) =>
        throw new AiDisabledException();

    public Task<SuggestFlowResult> SuggestFlowAsync(SuggestFlowRequest request, CancellationToken ct = default) =>
        throw new AiDisabledException();
}
