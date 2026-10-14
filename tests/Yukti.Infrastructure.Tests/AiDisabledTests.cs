using Xunit;
using Yukti.Application.Ai;
using Yukti.Infrastructure.Ai;

namespace Yukti.Infrastructure.Tests;

/// <summary>
/// D5-T2: proves the disabled stand-in itself — every method raises
/// <see cref="AiDisabledException"/> immediately, with no HttpClient and no
/// reference to any provider type, so there is nothing in this class
/// capable of making an outbound call regardless of what's registered
/// elsewhere. The other half of the proof — that `Ai:Enabled=false` is what
/// gets this class registered instead of <see cref="OpenAiAssistantService"/>,
/// and that the three AI endpoints turn that into a 503 — is proven against
/// the real running API in the `ai-off` CI job (no WebApplicationFactory/
/// TestHost harness exists in this repo yet to do it as a unit test; see
/// DISC-9).
/// </summary>
public sealed class AiDisabledTests
{
    [Fact]
    public async Task GenerateAssertionsAsync_raises_AiDisabledException()
    {
        var service = new AiDisabledAssistantService();
        await Assert.ThrowsAsync<AiDisabledException>(
            () => service.GenerateAssertionsAsync(new GenerateAssertionsRequest("GET", "/x", 200, null)));
    }

    [Fact]
    public async Task HealSelectorAsync_raises_AiDisabledException()
    {
        var service = new AiDisabledAssistantService();
        await Assert.ThrowsAsync<AiDisabledException>(
            () => service.HealSelectorAsync(new HealSelectorRequest("#gone", null, null, null, null)));
    }

    [Fact]
    public async Task SuggestFlowAsync_raises_AiDisabledException()
    {
        var service = new AiDisabledAssistantService();
        await Assert.ThrowsAsync<AiDisabledException>(
            () => service.SuggestFlowAsync(new SuggestFlowRequest("check the login works", null, null)));
    }

    [Fact]
    public void AiDisabledException_message_names_AI_as_disabled_not_a_generic_failure()
    {
        var ex = new AiDisabledException();
        Assert.Contains("disabled", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
