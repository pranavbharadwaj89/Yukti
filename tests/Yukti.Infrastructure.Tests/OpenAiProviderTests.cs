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
/// D5-T1: proves the real <see cref="IAiProvider"/> — credential resolution
/// through <see cref="ICredentialResolver"/> (never config/env directly),
/// a typed <see cref="AiUnavailableException"/> on timeout or failure (never
/// an unhandled exception or a silent empty response), and that the
/// pre-existing ai.request.* metrics (OrchestrationTelemetry, declared
/// ahead of any emitter since FR-OBS-02) are now actually populated.
///
/// [Collection(AiProviderTelemetryCollection.Name)]: this class and
/// GeminiProviderTests both attach a MeterListener to the same
/// process-wide static OrchestrationTelemetry.Meter and assert an exact
/// measurement count — run in parallel (xUnit's default across classes),
/// each listener sees both tests' increments. Reproduced for real (not
/// theorized): "Assert.Single() Failure: The collection contained 2
/// matching items" when both timeout tests ran together. Same collection
/// name serializes them relative to each other without slowing down
/// anything else in the assembly.
/// </summary>
[Collection(AiProviderTelemetryCollection.Name)]
public sealed class OpenAiProviderTests
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

    private static OpenAiProvider BuildProvider(string? apiKey, HttpMessageHandler handler, out StubHandler stub)
    {
        stub = (StubHandler)handler;
        var config = new ConfigurationBuilder().Build();
        return new OpenAiProvider(new FakeCredentialResolver(apiKey), config, NullLogger<OpenAiProvider>.Instance, new HttpClient(handler));
    }

    private static HttpResponseMessage SuccessResponse(string content, int promptTokens = 10, int completionTokens = 20)
    {
        var json = $@"{{""choices"":[{{""message"":{{""content"":{System.Text.Json.JsonSerializer.Serialize(content)}}}}}],""usage"":{{""prompt_tokens"":{promptTokens},""completion_tokens"":{completionTokens}}}}}";
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static StructuredGenerationRequest Request(TimeSpan timeout = default) =>
        new("coder", "system prompt", "user content", "{}", Timeout: timeout);

    [Fact]
    public async Task Resolves_the_api_key_through_the_credential_resolver_not_config_or_env()
    {
        var handler = new StubHandler((req, _) =>
        {
            Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
            Assert.Equal("resolved-from-vault", req.Headers.Authorization!.Parameter);
            return Task.FromResult(SuccessResponse(@"{""ok"":true}"));
        });
        var provider = BuildProvider("resolved-from-vault", handler, out _);

        var response = await provider.GenerateStructuredAsync(Request(), CancellationToken.None);

        Assert.Equal(@"{""ok"":true}", response.RawText);
        Assert.NotNull(response.Parsed);
        Assert.Equal(10, response.InputTokens);
        Assert.Equal(20, response.OutputTokens);
    }

    [Fact]
    public async Task No_credential_resolved_raises_AiUnavailableException_naming_the_reference_not_a_bare_null()
    {
        var provider = BuildProvider(null, new StubHandler((_, _) => throw new InvalidOperationException("should never reach the network")), out _);

        var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => provider.GenerateStructuredAsync(Request(), CancellationToken.None));
        Assert.Contains("ai-openai-api-key", ex.Message);
    }

    [Fact]
    public async Task Non_success_status_raises_AiUnavailableException_not_an_unhandled_exception()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("rate limited"),
        }));
        var provider = BuildProvider("key", handler, out _);

        var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => provider.GenerateStructuredAsync(Request(), CancellationToken.None));
        Assert.Contains("429", ex.Message);
    }

    [Fact]
    public async Task Timeout_raises_AiUnavailableException_and_increments_the_timeout_metric()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct); // outlives the request's own short timeout below
            return SuccessResponse("unreachable");
        });
        var provider = BuildProvider("key", handler, out _);

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
    public async Task Cancelling_the_callers_own_token_propagates_OperationCanceledException_not_AiUnavailableException()
    {
        // Distinct from the timeout case above: here the CALLER cancelled
        // (e.g. the flow run itself stopped), not our own 15s deadline. That
        // must bubble up as cooperative cancellation, never get wrapped into
        // "the AI provider is unavailable" — a caller handling cancellation
        // and a caller handling a real AI outage need to tell them apart.
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return SuccessResponse("unreachable");
        });
        var provider = BuildProvider("key", handler, out _);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // HttpClient throws its own TaskCanceledException (a subtype) for
        // this, not the base type exactly — xUnit's ThrowsAsync<T> requires
        // an exact type match, so this asserts "is-a" via IsAssignableFrom
        // instead of asserting the production code throws the base type
        // literally, which it has no reason to.
        var ex = await Record.ExceptionAsync(() => provider.GenerateStructuredAsync(Request(), cts.Token));
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
    }

    [Fact]
    public async Task A_response_that_is_not_valid_json_returns_null_Parsed_without_throwing()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(SuccessResponse("not json at all")));
        var provider = BuildProvider("key", handler, out _);

        var response = await provider.GenerateStructuredAsync(Request(), CancellationToken.None);

        Assert.Equal("not json at all", response.RawText);
        Assert.Null(response.Parsed);
    }

    [Fact]
    public void No_production_source_outside_Infrastructure_Ai_and_composition_roots_mentions_OpenAI()
    {
        // D5-T1: "No code outside the provider implementations references an
        // OpenAI type." OpenAiAssistantService.cs (pre-existing, unrelated
        // endpoints) and OpenAiProvider.cs (this task) are the only files
        // allowed to know OpenAI exists at all — business logic never does.
        // Program.cs files (composition roots) are the one standard
        // exception in a DI-based architecture: something has to map
        // IAiAssistantService -> OpenAiAssistantService once, by name, and
        // that's wiring, not a capability depending on a concrete provider.
        var srcDir = Path.Combine(FindRepoRoot(), "src");
        var allowedDir = Path.Combine(srcDir, "Yukti.Infrastructure", "Ai");

        var offending = Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !Path.GetDirectoryName(f)!.Equals(allowedDir, StringComparison.OrdinalIgnoreCase))
            .Where(f => !Path.GetFileName(f).Equals("Program.cs", StringComparison.OrdinalIgnoreCase))
            .Where(f => File.ReadAllText(f).Contains("openai", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(offending);
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Yukti.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Could not locate repo root (Yukti.sln) from " + AppContext.BaseDirectory);
    }
}
