using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yukti.Application.Ai;
using Yukti.Contracts;
using Yukti.Infrastructure.Ai;
using Yukti.Orchestration;

namespace Yukti.Infrastructure.Tests;

/// <summary>
/// ADR-0004: Gemini, not OpenAI, is the real provider — the owner has no
/// OpenAI key. Mirrors OpenAiProviderTests' structure exactly (the same
/// contract, IAiProvider, is under test) adapted for Gemini's own
/// request/response shape: key in the query string, not a Bearer header;
/// candidates[0].content.parts[0].text, not choices[0].message.content;
/// usageMetadata.promptTokenCount/candidatesTokenCount, not usage.prompt_tokens/completion_tokens.
///
/// [Collection(AiProviderTelemetryCollection.Name)]: see OpenAiProviderTests'
/// identical attribute for why — both classes' timeout tests race the same
/// static Meter otherwise, reproduced for real.
/// </summary>
[Collection(AiProviderTelemetryCollection.Name)]
public sealed class GeminiProviderTests
{
    private sealed class FakeCredentialResolver : ICredentialResolver
    {
        private readonly string? _value;
        public FakeCredentialResolver(string? value) => _value = value;
        public Task<string?> ResolveAsync(string credentialReference, CancellationToken ct) => Task.FromResult(_value);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;
        public HttpRequestMessage? LastRequest { get; private set; }

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return _respond(request, ct);
        }
    }

    private static GeminiProvider BuildProvider(string? apiKey, HttpMessageHandler handler)
    {
        var config = new ConfigurationBuilder().Build();
        return new GeminiProvider(new FakeCredentialResolver(apiKey), config, NullLogger<GeminiProvider>.Instance, new HttpClient(handler));
    }

    private static HttpResponseMessage SuccessResponse(string text, int promptTokens = 10, int candidatesTokens = 20)
    {
        var json = $@"{{""candidates"":[{{""content"":{{""parts"":[{{""text"":{System.Text.Json.JsonSerializer.Serialize(text)}}}]}}}}],""usageMetadata"":{{""promptTokenCount"":{promptTokens},""candidatesTokenCount"":{candidatesTokens}}}}}";
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static StructuredGenerationRequest Request(TimeSpan timeout = default) =>
        new("coder", "system prompt", "user content", "{}", Timeout: timeout);

    [Fact]
    public async Task Resolves_the_api_key_through_the_credential_resolver_and_puts_it_in_the_query_string_not_a_header()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler((req, _) =>
        {
            captured = req;
            return Task.FromResult(SuccessResponse(@"{""ok"":true}"));
        });
        var provider = BuildProvider("resolved-from-vault", handler);

        var response = await provider.GenerateStructuredAsync(Request(), CancellationToken.None);

        Assert.Null(captured!.Headers.Authorization);
        Assert.Contains("key=resolved-from-vault", captured.RequestUri!.Query);
        Assert.Equal(@"{""ok"":true}", response.RawText);
        Assert.NotNull(response.Parsed);
        Assert.Equal(10, response.InputTokens);
        Assert.Equal(20, response.OutputTokens);
    }

    [Fact]
    public async Task No_credential_resolved_raises_AiUnavailableException_naming_the_reference()
    {
        var provider = BuildProvider(null, new StubHandler((_, _) => throw new InvalidOperationException("should never reach the network")));

        var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => provider.GenerateStructuredAsync(Request(), CancellationToken.None));
        Assert.Contains("ai-gemini-api-key", ex.Message);
    }

    [Fact]
    public async Task Non_success_status_raises_AiUnavailableException()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("rate limited"),
        }));
        var provider = BuildProvider("key", handler);

        var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => provider.GenerateStructuredAsync(Request(), CancellationToken.None));
        Assert.Contains("429", ex.Message);
    }

    [Fact]
    public async Task Timeout_raises_AiUnavailableException_and_increments_the_timeout_metric()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return SuccessResponse("unreachable");
        });
        var provider = BuildProvider("key", handler);

        var measurements = new List<(string Instrument, long Value)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OrchestrationTelemetry.SourceName && instrument.Name == "ai.request.timeout")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => measurements.Add((instrument.Name, value)));
        listener.Start();

        await Assert.ThrowsAsync<AiUnavailableException>(
            () => provider.GenerateStructuredAsync(Request(TimeSpan.FromMilliseconds(50)), CancellationToken.None));

        listener.Dispose();
        Assert.Single(measurements, m => m.Instrument == "ai.request.timeout");
    }

    [Fact]
    public async Task Cancelling_the_callers_own_token_propagates_OperationCanceledException()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return SuccessResponse("unreachable");
        });
        var provider = BuildProvider("key", handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ex = await Record.ExceptionAsync(() => provider.GenerateStructuredAsync(Request(), cts.Token));
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
    }

    [Fact]
    public async Task A_response_that_is_not_valid_json_returns_null_Parsed_without_throwing()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(SuccessResponse("not json at all")));
        var provider = BuildProvider("key", handler);

        var response = await provider.GenerateStructuredAsync(Request(), CancellationToken.None);

        Assert.Equal("not json at all", response.RawText);
        Assert.Null(response.Parsed);
    }

    [Fact]
    public async Task An_empty_or_filtered_candidate_falls_through_to_empty_text_without_throwing()
    {
        // Gemini's own failure shape for a content-filter refusal: a
        // candidate with no "content" property at all, not an HTTP error.
        var handler = new StubHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(@"{""candidates"":[{""finishReason"":""SAFETY""}]}", Encoding.UTF8, "application/json"),
            }));
        var provider = BuildProvider("key", handler);

        var response = await provider.GenerateStructuredAsync(Request(), CancellationToken.None);

        Assert.Equal("", response.RawText);
        Assert.Null(response.Parsed);
    }

    [Fact]
    public void Id_is_gemini()
    {
        Assert.Equal("gemini", new GeminiProvider(new FakeCredentialResolver("k"),
            new ConfigurationBuilder().Build(), NullLogger<GeminiProvider>.Instance, new HttpClient(new StubHandler((_, _) =>
                Task.FromResult(SuccessResponse("{}"))))).Id);
    }
}
