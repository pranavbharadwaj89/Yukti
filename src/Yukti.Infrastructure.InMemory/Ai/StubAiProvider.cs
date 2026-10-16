using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Yukti.Application.Ai;

namespace Yukti.Infrastructure.InMemory.Ai;

/// <summary>
/// D5-T1: the only <see cref="IAiProvider"/> tests are allowed to call —
/// never the network. A fixture is picked by <see
/// cref="StructuredGenerationRequest.Capability"/> ("analyst", "coder", ...),
/// set with <see cref="SetFixture"/>; a capability with no fixture raises
/// the same <see cref="AiUnavailableException"/> a real provider outage
/// would, so tests for "the provider is unavailable" don't need a second code path.
/// </summary>
public sealed class StubAiProvider : IAiProvider
{
    // ConcurrentDictionary, not Dictionary: if this ever ends up registered
    // as a shared (singleton/DI-scoped) instance rather than one-per-test,
    // a SetFixture racing a GenerateStructuredAsync read must not corrupt
    // the dictionary or throw — a flaky test failure here would be a false
    // positive about the AI capability under test, not about this stub.
    private readonly ConcurrentDictionary<string, string> _fixturesByCapability = new();

    public string Id => "stub";

    /// <summary>The most recent request this stub received — lets a capability's own tests
    /// (Analyst, Coder) assert on exactly what prompt/context was actually sent, e.g. the
    /// token-economy spec's §R2 "no ParamSpec fields reached the prompt" checks.</summary>
    public StructuredGenerationRequest? LastRequest { get; private set; }

    public void SetFixture(string capability, string rawJson) => _fixturesByCapability[capability] = rawJson;

    public Task<ProviderResponse> GenerateStructuredAsync(StructuredGenerationRequest request, CancellationToken ct)
    {
        LastRequest = request;

        if (!_fixturesByCapability.TryGetValue(request.Capability, out var raw))
        {
            throw new AiUnavailableException(
                $"StubAiProvider has no fixture registered for capability '{request.Capability}'. " +
                "Call SetFixture before exercising this capability in a test.");
        }

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(raw);
        }
        catch (JsonException)
        {
            parsed = null;
        }

        // Rough, stable-enough-for-a-stub token estimate — real counts come
        // from the real provider's own usage field; this only needs to be
        // non-zero so cost-record tests have something to assert against.
        var estimatedTokens = Math.Max(1, raw.Length / 4);

        return Task.FromResult(new ProviderResponse(raw, parsed, estimatedTokens, estimatedTokens, TimeSpan.FromMilliseconds(5)));
    }
}
