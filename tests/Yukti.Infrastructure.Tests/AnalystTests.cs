using System.Text.Json;
using Xunit;
using Yukti.Application.Ai;
using Yukti.Contracts;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.Ai;
using Yukti.Orchestration;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Infrastructure.Tests;

/// <summary>
/// D5-T4: English + registered modules → <see cref="ParsedIntent"/>. Out-of-scope and
/// two-module-ambiguous requests both produce a named ambiguity, never a guess — exercised here
/// with the real <see cref="AiPlanningEngine"/> (D5-T3) and <see cref="StubAiProvider"/> +
/// recorded fixtures, per the task's own ✔ command. Also covers token-economy spec §R2 (Analyst
/// gets a compact registry summary — kind, action names, one-line description, no
/// <see cref="ParamSpec"/> detail) and §R4 (Analyst's configured caps, seeded in appsettings.json,
/// are honoured rather than a literal).
/// </summary>
public sealed class AnalystTests
{
    /// <summary>A throwaway module with one action that has real ParamSpecs — proving §R2 strips
    /// them from Analyst's context, not just that the context happens to omit them by accident.</summary>
    private sealed class FakeWebModule : IAutomationModule
    {
        public static readonly ModuleKind Kind = ModuleKind.Custom("web");
        ModuleKind IAutomationModule.Kind => Kind;
        public string ContractVersion => "1.0.0";

        public IReadOnlyList<ActionSchema> GetSupportedActions() => new[]
        {
            new ActionSchema
            {
                ActionName = "navigate",
                Description = "Navigate the browser to a URL.",
                Parameters = new[]
                {
                    new ParamSpec { Name = "url", Type = ParamType.String, Required = true, Description = "The URL to navigate to." },
                },
            },
        };

        public Task Setup(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task Teardown(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task<StepOutcome> Run(string action, IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct) =>
            throw new NotSupportedException("Analyst never dispatches actions.");
    }

    private sealed class FakeApiModule : IAutomationModule
    {
        public static readonly ModuleKind Kind = ModuleKind.Custom("api");
        ModuleKind IAutomationModule.Kind => Kind;
        public string ContractVersion => "1.0.0";

        public IReadOnlyList<ActionSchema> GetSupportedActions() => new[]
        {
            new ActionSchema { ActionName = "get", Description = "Send a GET request and assert on the response.", Parameters = Array.Empty<ParamSpec>() },
        };

        public Task Setup(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task Teardown(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task<StepOutcome> Run(string action, IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct) =>
            throw new NotSupportedException("Analyst never dispatches actions.");
    }

    private static ModuleRegistry BuildRegistry()
    {
        var registry = new ModuleRegistry();
        registry.Register(new FakeWebModule());
        registry.Register(new FakeApiModule());
        return registry;
    }

    private static (Analyst Analyst, Yukti.Infrastructure.InMemory.Ai.StubAiProvider Provider, ModuleRegistry Registry) BuildAnalyst(
        IAiCapabilityOptionsProvider? capabilityOptions = null)
    {
        var provider = new Yukti.Infrastructure.InMemory.Ai.StubAiProvider();
        var registry = BuildRegistry();
        var promptLoader = new FixedPromptLoader("Analyst instructions.\n{{CONTEXT}}");
        var engine = new AiPlanningEngine(provider, promptLoader, new NoOpCostRecorder(),
            capabilityOptions ?? new FixedCapabilityOptions(), new AlwaysAllowedBudgetGuard(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AiPlanningEngine>.Instance);
        var analyst = new Analyst(engine, registry);
        return (analyst, provider, registry);
    }

    private sealed class FixedPromptLoader : IPromptLoader
    {
        private readonly string _template;
        public FixedPromptLoader(string template) => _template = template;
        public Task<string> LoadAsync(string capability, string version, CancellationToken ct) => Task.FromResult(_template);
    }

    private sealed class NoOpCostRecorder : IAiCostRecorder
    {
        public Task RecordAsync(AiCostRecordData record, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class AlwaysAllowedBudgetGuard : IAiBudgetGuard
    {
        public Task<AiBudgetCheck> CheckAsync(TenantId tenantId, string provider, int estimatedInputTokens, int maxOutputTokens, CancellationToken ct) =>
            Task.FromResult(new AiBudgetCheck(Allowed: true, NearCap: false, SpentUsd: 0m, ProjectedUsd: 0m, CapUsd: 1000m));
    }

    private sealed class FixedCapabilityOptions : IAiCapabilityOptionsProvider
    {
        public AiCapabilityOptions GetFor(string capability) => AiCapabilityOptions.Default;
        public int MaxValidationRetries => 1;
    }

    [Fact]
    public async Task A_multi_module_request_returns_every_genuinely_needed_module_with_no_ambiguity()
    {
        var (analyst, provider, _) = BuildAnalyst();
        provider.SetFixture(Analyst.Capability, """
            {"candidateModuleKinds":["web","api"],"assertions":["the orders API returns status confirmed"],"ambiguities":[]}
            """);

        var intent = await analyst.AnalyzeAsync("log in on the web, then check the orders API", TenantId.New(), CancellationToken.None);

        Assert.Equal(new[] { "web", "api" }, intent.CandidateModuleKinds);
        Assert.Single(intent.Assertions);
        Assert.Empty(intent.Ambiguities);
    }

    [Fact]
    public async Task An_out_of_scope_request_produces_a_named_ambiguity_with_no_candidate_modules()
    {
        var (analyst, provider, _) = BuildAnalyst();
        provider.SetFixture(Analyst.Capability, """
            {"candidateModuleKinds":[],"assertions":[],"ambiguities":["No registered module can send SMS messages."]}
            """);

        var intent = await analyst.AnalyzeAsync("send an SMS to the on-call engineer", TenantId.New(), CancellationToken.None);

        Assert.Empty(intent.CandidateModuleKinds);
        Assert.Single(intent.Ambiguities);
    }

    [Fact]
    public async Task A_two_module_ambiguous_request_produces_a_named_ambiguity_instead_of_a_guess()
    {
        var (analyst, provider, _) = BuildAnalyst();
        provider.SetFixture(Analyst.Capability, """
            {"candidateModuleKinds":[],"assertions":[],"ambiguities":["Could mean checking via the api module or the web module - unclear which."]}
            """);

        var intent = await analyst.AnalyzeAsync("check that it worked", TenantId.New(), CancellationToken.None);

        Assert.Empty(intent.CandidateModuleKinds);
        Assert.Single(intent.Ambiguities);
    }

    [Fact]
    public async Task A_hallucinated_module_kind_fails_validation_on_every_attempt_and_names_it()
    {
        // StubAiProvider always answers a capability with the same fixture, so this proves the
        // validator genuinely rejects an unregistered kind (both the first attempt and the
        // engine's own retry, D5-T3) rather than ever accepting it — the engine's retry-then-throw
        // behavior itself is already proven generically in AiPlanningEngineTests.
        var (analyst, provider, _) = BuildAnalyst();
        provider.SetFixture(Analyst.Capability, """
            {"candidateModuleKinds":["sms"],"assertions":[],"ambiguities":[]}
            """);

        var ex = await Assert.ThrowsAsync<AiGenerationFailedException>(
            () => analyst.AnalyzeAsync("text someone", TenantId.New(), CancellationToken.None));

        Assert.Contains(ex.ValidationErrors, e => e.Contains("sms"));
    }

    [Fact]
    public async Task Empty_candidates_and_empty_ambiguities_together_fails_validation_never_passes_silently()
    {
        var (analyst, provider, _) = BuildAnalyst();
        provider.SetFixture(Analyst.Capability, """{"candidateModuleKinds":[],"assertions":[],"ambiguities":[]}""");

        var ex = await Assert.ThrowsAsync<AiGenerationFailedException>(
            () => analyst.AnalyzeAsync("do something", TenantId.New(), CancellationToken.None));

        Assert.Contains(ex.ValidationErrors, e => e.Contains("never guess", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_registry_context_sent_to_the_provider_has_no_ParamSpec_fields()
    {
        // §R2: "Analyst receives a compact registry summary only: module kind, action names,
        // one-line description. No ParamSpec detail, no examples."
        var (analyst, provider, _) = BuildAnalyst();
        provider.SetFixture(Analyst.Capability, """{"candidateModuleKinds":["web"],"assertions":[],"ambiguities":[]}""");

        await analyst.AnalyzeAsync("navigate somewhere", TenantId.New(), CancellationToken.None);

        var sentPrompt = provider.LastRequest!.SystemPrompt;
        Assert.Contains("\"kind\":\"web\"", sentPrompt);
        Assert.Contains("\"navigate\"", sentPrompt);
        Assert.DoesNotContain("required", sentPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("defaultValue", sentPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("parameters", sentPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("the URL to navigate to", sentPrompt); // the param-level description never leaks in
    }

    [Fact]
    public async Task The_registry_context_is_minified_JSON_with_a_stable_key_order_across_calls()
    {
        var (analyst, provider, _) = BuildAnalyst();
        provider.SetFixture(Analyst.Capability, """{"candidateModuleKinds":["web"],"assertions":[],"ambiguities":[]}""");

        await analyst.AnalyzeAsync("first call", TenantId.New(), CancellationToken.None);
        var first = provider.LastRequest!.SystemPrompt;
        await analyst.AnalyzeAsync("second call", TenantId.New(), CancellationToken.None);
        var second = provider.LastRequest!.SystemPrompt;

        Assert.Equal(first, second);
        Assert.DoesNotContain("\n  ", first); // no pretty-printed indentation — minified
    }

    [Fact]
    public async Task The_assembled_prompt_stays_within_Analysts_configured_MaxInputChars()
    {
        // §R2/R4: the per-fixture prompt size is asserted against the configured cap so a future
        // change that bloats the registry context fails loudly here, not silently in production.
        var analystCap = new AiCapabilityOptions(MaxInputChars: 6000, MaxOutputTokens: 1024, Temperature: 0);
        var (analyst, provider, _) = BuildAnalyst(new ConstantCapabilityOptions(analystCap));
        provider.SetFixture(Analyst.Capability, """{"candidateModuleKinds":["web"],"assertions":[],"ambiguities":[]}""");

        await analyst.AnalyzeAsync("a perfectly ordinary request", TenantId.New(), CancellationToken.None);

        var sentPrompt = provider.LastRequest!.SystemPrompt;
        Assert.True(sentPrompt.Length <= analystCap.MaxInputChars,
            $"assembled prompt was {sentPrompt.Length} chars, expected <= {analystCap.MaxInputChars}");
    }

    private sealed class ConstantCapabilityOptions : IAiCapabilityOptionsProvider
    {
        private readonly AiCapabilityOptions _options;
        public ConstantCapabilityOptions(AiCapabilityOptions options) => _options = options;
        public AiCapabilityOptions GetFor(string capability) => _options;
        public int MaxValidationRetries => 1;
    }
}
