using System.Diagnostics;
using System.Net.Http.Headers;
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
/// D5-T1: the real <see cref="IAiProvider"/> — OpenAI, per the owner's
/// 2026-10-14 vendor call (plan/adr/0003-ai-vendor-openai.md): keep the
/// existing, working OpenAI integration rather than throw it away for
/// Anthropic. Deliberately a sibling of <see cref="OpenAiAssistantService"/>,
/// not a refactor of it — that class keeps serving its three existing
/// capability-specific endpoints unchanged; this one is only ever called by
/// the new Sarvagna planning pipeline (D5-T3 onward).
///
/// The API key is resolved through <see cref="ICredentialResolver"/> (the
/// same Vault-backed resolver every module's credentials go through) —
/// never read from IConfiguration or an environment variable directly,
/// unlike the older <see cref="OpenAiAssistantService"/>.
/// </summary>
public sealed class OpenAiProvider : IAiProvider
{
    private readonly ICredentialResolver _credentialResolver;
    private readonly ILogger<OpenAiProvider> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly string _endpoint;
    private readonly string _credentialReference;

    public string Id => "openai";

    public OpenAiProvider(
        ICredentialResolver credentialResolver,
        IConfiguration config,
        ILogger<OpenAiProvider> logger,
        HttpClient? httpClient = null)
    {
        _credentialResolver = credentialResolver;
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
        _model = config["Ai:OpenAi:Model"] ?? "gpt-4o-mini";
        _endpoint = config["Ai:OpenAi:Endpoint"] ?? "https://api.openai.com/v1/chat/completions";
        _credentialReference = config["Ai:OpenAi:CredentialReference"] ?? "ai-openai-api-key";
    }

    public async Task<ProviderResponse> GenerateStructuredAsync(StructuredGenerationRequest request, CancellationToken ct)
    {
        var apiKey = await _credentialResolver.ResolveAsync(_credentialReference, ct);
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new AiUnavailableException(
                $"No AI provider credential resolved for reference '{_credentialReference}'. " +
                "Put the OpenAI API key in Vault under this reference before AI generation can run.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(request.EffectiveTimeout);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Content = JsonContent.Create(new
        {
            model = _model,
            max_tokens = request.MaxTokens,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserContent },
            },
        });

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            OrchestrationTelemetry.AiRequestTimeout.Add(1);
            OrchestrationTelemetry.AiRequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
            _logger.LogWarning("OpenAI request for capability {Capability} timed out after {TimeoutSeconds}s",
                request.Capability, request.EffectiveTimeout.TotalSeconds);
            throw new AiUnavailableException($"OpenAI request timed out after {request.EffectiveTimeout.TotalSeconds:F0}s.");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            OrchestrationTelemetry.AiRequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
            throw new AiUnavailableException("OpenAI request failed.", ex);
        }

        stopwatch.Stop();
        OrchestrationTelemetry.AiRequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("OpenAI responded {StatusCode} for capability {Capability}: {Body}",
                (int)response.StatusCode, request.Capability, errorBody);
            throw new AiUnavailableException($"OpenAI responded with {(int)response.StatusCode} {response.StatusCode}.");
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        using var envelope = JsonDocument.Parse(body);
        var content = envelope.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";

        var inputTokens = 0;
        var outputTokens = 0;
        if (envelope.RootElement.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            if (usage.TryGetProperty("prompt_tokens", out var pt)) inputTokens = pt.GetInt32();
            if (usage.TryGetProperty("completion_tokens", out var ot)) outputTokens = ot.GetInt32();
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

        return new ProviderResponse(content, parsed, inputTokens, outputTokens, stopwatch.Elapsed);
    }
}
