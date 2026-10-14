using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Yukti.Application.Ai;
using Yukti.Contracts;
using Yukti.Orchestration;

namespace Yukti.Infrastructure.Ai;

/// <summary>
/// D5-T1/ADR-0004: the real <see cref="IAiProvider"/> — Gemini, not OpenAI
/// (the owner has no OpenAI key; Gemini is already proven working in this
/// repo via <c>sarvaghna/core/gemini-client.mjs</c>, the Sarvaghna crew's
/// own caller). Mirrors that script's exact REST shape — same endpoint
/// pattern, same `responseMimeType: "application/json"` structured-output
/// request — but resolves its key through <see cref="ICredentialResolver"/>
/// (Vault), not an env var read directly, and raises the typed
/// <see cref="AiUnavailableException"/> this codebase's IAiProvider
/// contract already expects instead of that script's {ok, reason} style.
///
/// Structurally a sibling of <see cref="OpenAiProvider"/>, kept in place
/// (not deleted) for if an OpenAI key ever exists — switching back is a DI
/// registration change, not a rewrite.
/// </summary>
public sealed class GeminiProvider : IAiProvider
{
    private readonly ICredentialResolver _credentialResolver;
    private readonly ILogger<GeminiProvider> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly string _endpointBase;
    private readonly string _credentialReference;

    public string Id => "gemini";

    public GeminiProvider(
        ICredentialResolver credentialResolver,
        IConfiguration config,
        ILogger<GeminiProvider> logger,
        HttpClient? httpClient = null)
    {
        _credentialResolver = credentialResolver;
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
        _model = config["Ai:Gemini:Model"] ?? "gemini-3.8-flash";
        _endpointBase = config["Ai:Gemini:Endpoint"] ?? "https://generativelanguage.googleapis.com/v1beta/models";
        _credentialReference = config["Ai:Gemini:CredentialReference"] ?? "ai-gemini-api-key";
    }

    public async Task<ProviderResponse> GenerateStructuredAsync(StructuredGenerationRequest request, CancellationToken ct)
    {
        var apiKey = await _credentialResolver.ResolveAsync(_credentialReference, ct);
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new AiUnavailableException(
                $"No AI provider credential resolved for reference '{_credentialReference}'. " +
                "Put the Gemini API key in Vault under this reference before AI generation can run.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(request.EffectiveTimeout);

        // The key goes in the query string (Gemini's own convention, not a
        // Bearer header — confirmed against gemini-client.mjs, not guessed).
        var endpoint = $"{_endpointBase}/{_model}:generateContent?key={Uri.EscapeDataString(apiKey)}";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Content = JsonContent.Create(new
        {
            systemInstruction = new { parts = new[] { new { text = request.SystemPrompt } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = request.UserContent } } } },
            generationConfig = new
            {
                temperature = 0.2,
                maxOutputTokens = request.MaxTokens,
                responseMimeType = "application/json",
            },
        });

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _httpClient.SendAsync(httpRequest, timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                stopwatch.Stop();
                OrchestrationTelemetry.AiRequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
                _logger.LogWarning("Gemini responded {StatusCode} for capability {Capability}: {Body}",
                    (int)response.StatusCode, request.Capability, errorBody);
                throw new AiUnavailableException($"Gemini responded with {(int)response.StatusCode} {response.StatusCode}.");
            }

            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            stopwatch.Stop();
            OrchestrationTelemetry.AiRequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
            return ParseGenerateContentResponse(body, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            OrchestrationTelemetry.AiRequestTimeout.Add(1);
            OrchestrationTelemetry.AiRequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
            _logger.LogWarning("Gemini request for capability {Capability} timed out after {TimeoutSeconds}s",
                request.Capability, request.EffectiveTimeout.TotalSeconds);
            throw new AiUnavailableException($"Gemini request timed out after {request.EffectiveTimeout.TotalSeconds:F0}s.");
        }
        catch (OperationCanceledException)
        {
            // The caller's own token, not our timeout — cooperative
            // cancellation must propagate as-is (see OpenAiProvider's
            // identical reasoning, confirmed via its own Sarvaghna review).
            throw;
        }
        catch (AiUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            OrchestrationTelemetry.AiRequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
            throw new AiUnavailableException("Gemini request failed.", ex);
        }
    }

    /// <summary>
    /// Gemini wraps an empty/filtered result as a candidate with no
    /// `content.parts` rather than an HTTP error — defensive TryGetProperty
    /// throughout so that reads as an empty string (a planning-pipeline
    /// schema-validation failure, D5-T3) rather than an unhandled exception
    /// from this provider layer.
    /// </summary>
    private static ProviderResponse ParseGenerateContentResponse(string body, TimeSpan elapsed)
    {
        using var envelope = JsonDocument.Parse(body);
        var root = envelope.RootElement;

        var content = root.TryGetProperty("candidates", out var candidates)
            && candidates.GetArrayLength() > 0
            && candidates[0].TryGetProperty("content", out var candidateContent)
            && candidateContent.TryGetProperty("parts", out var parts)
            && parts.GetArrayLength() > 0
            && parts[0].TryGetProperty("text", out var textProp)
                ? textProp.GetString() ?? ""
                : "";

        var inputTokens = 0;
        var outputTokens = 0;
        if (root.TryGetProperty("usageMetadata", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            if (usage.TryGetProperty("promptTokenCount", out var pt)) inputTokens = pt.GetInt32();
            if (usage.TryGetProperty("candidatesTokenCount", out var ct2)) outputTokens = ct2.GetInt32();
        }

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(content);
        }
        catch (JsonException)
        {
            parsed = null;
        }

        return new ProviderResponse(content, parsed, inputTokens, outputTokens, elapsed);
    }
}
