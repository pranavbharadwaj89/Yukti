using System.Text.Json.Nodes;

namespace Yukti.Application.Ai;

/// <summary>
/// Day 5 (Sarvagna): the provider-abstracted port every AI capability
/// agent (Analyst, Coder) calls through — one generic "ask a model for
/// structured JSON" operation, never a capability-specific method. This is
/// deliberately a different, lower-level contract than the pre-existing
/// <see cref="IAiAssistantService"/> (assertions/selector-heal/flow-suggest):
/// that interface stays as-is for its three existing endpoints; this one is
/// what the new planning pipeline (D5-T3 onward) is built on.
///
/// Vendor decision (owner, 2026-10-14, recorded in ADR-0003 under plan/adr/):
/// which vendor sits behind this interface is an Infrastructure-layer
/// detail — see the concrete class this solution registers in Program.cs.
/// </summary>
public interface IAiProvider
{
    /// <summary>A short, stable identifier (the real provider's own name, or "stub") — recorded on every AiAttribution and cost record.</summary>
    string Id { get; }

    Task<ProviderResponse> GenerateStructuredAsync(StructuredGenerationRequest request, CancellationToken ct);
}

/// <summary>
/// Everything a provider needs to make one structured-generation call.
/// <paramref name="Capability"/> names which Sarvagna agent is calling
/// ("analyst", "coder", ...) — used for cost records and by
/// <see cref="Yukti.Infrastructure.InMemory.Ai.StubAiProvider"/> to pick a fixture.
/// </summary>
public sealed record StructuredGenerationRequest(
    string Capability,
    string SystemPrompt,
    string UserContent,
    string JsonSchema,
    int MaxTokens = 2000,
    TimeSpan Timeout = default)
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Timeout to actually use — <see cref="Timeout"/>'s struct default (zero) means "use the 15s default", not "no timeout".</summary>
    public TimeSpan EffectiveTimeout => Timeout == default ? DefaultTimeout : Timeout;
}

/// <summary>
/// <paramref name="Parsed"/> is non-null only when <paramref name="RawText"/> was
/// well-formed JSON — this provider layer never validates it against
/// <see cref="StructuredGenerationRequest.JsonSchema"/>; that's the planning
/// pipeline's job (D5-T3), so there is exactly one place schema validation happens.
/// </summary>
public sealed record ProviderResponse(
    string RawText,
    JsonNode? Parsed,
    int InputTokens,
    int OutputTokens,
    TimeSpan Latency);

/// <summary>
/// Raised whenever a provider cannot produce a response at all — timeout, no
/// credential resolved, the provider's own API returning an error. Never
/// raised for "the model returned something, but it didn't parse" — that's
/// a <see cref="ProviderResponse"/> with a null <c>Parsed</c>, not an exception.
/// </summary>
public sealed class AiUnavailableException : Exception
{
    public AiUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}
