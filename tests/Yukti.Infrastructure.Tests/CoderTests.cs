using System.Linq;
using Xunit;
using Yukti.Application.Ai;
using Yukti.Contracts;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.Ai;
using Yukti.Orchestration;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Infrastructure.Tests;

/// <summary>
/// D5-T5: <see cref="ParsedIntent"/> → a Draft <see cref="Flow"/>, validated by Day 2's own
/// <see cref="Flow.Publish"/> (never a second copy of its rules) — exercised here with the real
/// <see cref="AiPlanningEngine"/> (D5-T3), <see cref="StubAiProvider"/> + recorded fixtures, and a
/// real <see cref="Flow"/> aggregate, per the task's own ✔ command. Also covers the token-economy
/// spec §R2 (Coder gets full <see cref="ActionSchema"/> for only the candidate modules named in
/// <see cref="ParsedIntent"/>, never the whole registry) and §R4 (Coder's configured caps).
/// </summary>
public sealed class CoderTests
{
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
                Parameters = new[] { new ParamSpec { Name = "url", Type = ParamType.String, Required = true, Description = "The URL to navigate to." } },
            },
        };

        public Task Setup(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task Teardown(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task<StepOutcome> Run(string action, IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct) =>
            throw new NotSupportedException("Coder never dispatches actions.");
    }

    private sealed class FakeApiModule : IAutomationModule
    {
        public static readonly ModuleKind Kind = ModuleKind.Custom("api");
        ModuleKind IAutomationModule.Kind => Kind;
        public string ContractVersion => "1.0.0";

        public IReadOnlyList<ActionSchema> GetSupportedActions() => new[]
        {
            new ActionSchema
            {
                ActionName = "get",
                Description = "Send a GET request and assert on the response.",
                Parameters = new[] { new ParamSpec { Name = "url", Type = ParamType.String, Required = true, Description = "The URL to GET." } },
            },
        };

        public Task Setup(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task Teardown(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task<StepOutcome> Run(string action, IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct) =>
            throw new NotSupportedException("Coder never dispatches actions.");
    }

    /// <summary>A third, registered-but-not-a-candidate module — proves §R2 excludes it from the
    /// prompt entirely, the difference between "only the candidates" and "the whole registry."</summary>
    private sealed class FakeLogsModule : IAutomationModule
    {
        public static readonly ModuleKind Kind = ModuleKind.Custom("logs");
        ModuleKind IAutomationModule.Kind => Kind;
        public string ContractVersion => "1.0.0";

        public IReadOnlyList<ActionSchema> GetSupportedActions() => new[]
        {
            new ActionSchema { ActionName = "checkRules", Description = "Never sent to Coder for this intent.", Parameters = Array.Empty<ParamSpec>() },
        };

        public Task Setup(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task Teardown(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task<StepOutcome> Run(string action, IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct) =>
            throw new NotSupportedException("Coder never dispatches actions.");
    }

    private sealed class FakeModuleActionResolver : IModuleActionResolver
    {
        private readonly Dictionary<(ModuleKind, string), ActionSchema> _actions = new();

        public FakeModuleActionResolver Register(ModuleKind module, ActionSchema schema)
        {
            _actions[(module, schema.ActionName)] = schema;
            return this;
        }

        public Task<ActionSchema?> TryGetActionSchema(ModuleKind module, string action, CancellationToken ct) =>
            Task.FromResult(_actions.TryGetValue((module, action), out var schema) ? schema : null);
    }

    private static readonly TenantId TestTenant = new(Guid.NewGuid());
    private static readonly UserId TestUser = new(Guid.NewGuid());

    private static (Coder Coder, Yukti.Infrastructure.InMemory.Ai.StubAiProvider Provider) BuildCoder()
    {
        var registry = new ModuleRegistry();
        registry.Register(new FakeWebModule());
        registry.Register(new FakeApiModule());
        registry.Register(new FakeLogsModule());

        var resolver = new FakeModuleActionResolver()
            .Register(FakeWebModule.Kind, new ActionSchema { ActionName = "navigate", Parameters = new[] { new ParamSpec { Name = "url", Type = ParamType.String, Required = true } } })
            .Register(FakeApiModule.Kind, new ActionSchema { ActionName = "get", Parameters = new[] { new ParamSpec { Name = "url", Type = ParamType.String, Required = true } } });

        var provider = new Yukti.Infrastructure.InMemory.Ai.StubAiProvider();
        var engine = new AiPlanningEngine(provider, new FixedPromptLoader("Coder instructions.\n{{CONTEXT}}"),
            new NoOpCostRecorder(), new FixedCapabilityOptions(), new AlwaysAllowedBudgetGuard(), Microsoft.Extensions.Logging.Abstractions.NullLogger<AiPlanningEngine>.Instance);

        return (new Coder(engine, registry, resolver), provider);
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

    private sealed class FixedCapabilityOptions : IAiCapabilityOptionsProvider
    {
        public AiCapabilityOptions GetFor(string capability) => AiCapabilityOptions.Default;
        public int MaxValidationRetries => 1;
    }

    private sealed class AlwaysAllowedBudgetGuard : IAiBudgetGuard
    {
        public Task<AiBudgetCheck> CheckAsync(TenantId tenantId, string provider, int estimatedInputTokens, int maxOutputTokens, CancellationToken ct) =>
            Task.FromResult(new AiBudgetCheck(Allowed: true, NearCap: false, SpentUsd: 0m, ProjectedUsd: 0m, CapUsd: 1000m));
    }

    private static readonly ParsedIntent WebAndApiIntent = new(
        CandidateModuleKinds: ["web", "api"], Assertions: ["the orders API returns status confirmed"], Ambiguities: []);

    [Fact]
    public async Task A_valid_two_step_flow_is_returned_as_a_Draft_flow_never_Published()
    {
        var (coder, provider) = BuildCoder();
        provider.SetFixture(Coder.Capability, """
            {"name":"Check order","declaredVariables":[],"steps":[
              {"name":"Login","module":"web","action":"navigate","params":{"url":"https://x/login"},"saveAs":null,"when":null},
              {"name":"Check order","module":"api","action":"get","params":{"url":"https://x/orders/1"},"saveAs":null,"when":null}
            ]}
            """);

        var flow = await coder.GenerateAsync(WebAndApiIntent, TestTenant, TestUser, CancellationToken.None);

        Assert.Equal(FlowStatus.Draft, flow.Status);
        Assert.Equal("Check order", flow.Name);
        Assert.Equal(2, flow.Steps.Count);
        Assert.Equal(ModuleKind.Custom("web"), flow.Steps[0].Module);
        Assert.Equal(ModuleKind.Custom("api"), flow.Steps[1].Module);

        // D5-T6: every generated step carries real AiAttribution — capability, model, prompt
        // version, and a formula-based confidence (never a literal).
        foreach (var step in flow.Steps)
        {
            Assert.NotNull(step.AiAttribution);
            Assert.Equal(Coder.Capability, step.AiAttribution!.Capability);
            Assert.Equal("stub", step.AiAttribution.ModelIdentifier);
            Assert.Equal(Coder.PromptVersion, step.AiAttribution.PromptVersion);
            Assert.Equal(AiConfidence.FullConfidence, step.AiAttribution.Confidence);
        }
    }

    [Fact]
    public async Task A_manually_authored_step_carries_no_AiAttribution_the_AI_off_default()
    {
        // D5-T6: the other half of the contract — a step nobody ran through Coder (the ordinary
        // case, and the only case when AI is disabled, D5-T2) must carry no AiAttribution at all.
        // Flow.AddStep's new parameter is additive and optional precisely so every pre-existing
        // manual/CLI call site (FlowLoader, Program.cs's step-add endpoint) is unaffected.
        var flow = Flow.CreateDraft("Manual flow", description: null, TestTenant, TestUser);
        flow.AddStep("Login", ModuleKind.Custom("web"), "navigate", new Dictionary<string, object?> { ["url"] = "https://x" });

        Assert.Null(flow.Steps[0].AiAttribution);
    }

    [Fact]
    public async Task A_success_after_one_retry_attaches_lower_confidence_attribution()
    {
        // Proves confidence genuinely flows end-to-end from the engine's own retry outcome into
        // the attribution actually attached to the returned Flow's steps — not just that the
        // formula is correct in isolation (AiConfidenceTests already proves that).
        var registry = new ModuleRegistry();
        registry.Register(new FakeWebModule());
        registry.Register(new FakeApiModule());
        var resolver = new FakeModuleActionResolver()
            .Register(FakeWebModule.Kind, new ActionSchema { ActionName = "navigate", Parameters = new[] { new ParamSpec { Name = "url", Type = ParamType.String, Required = true } } });

        var badJson = """{"name":"Bad","declaredVariables":[],"steps":[{"name":"s","module":"web","action":"navigate","params":{},"saveAs":null,"when":null}]}""";
        var goodJson = """{"name":"Good","declaredVariables":[],"steps":[{"name":"s","module":"web","action":"navigate","params":{"url":"https://x"},"saveAs":null,"when":null}]}""";
        var provider = new SequencedProvider(badJson, goodJson);

        var engine = new AiPlanningEngine(provider, new FixedPromptLoader("Coder instructions.\n{{CONTEXT}}"),
            new NoOpCostRecorder(), new FixedCapabilityOptions(), new AlwaysAllowedBudgetGuard(), Microsoft.Extensions.Logging.Abstractions.NullLogger<AiPlanningEngine>.Instance);
        var coder = new Coder(engine, registry, resolver);

        var flow = await coder.GenerateAsync(new ParsedIntent(["web"], [], []), TestTenant, TestUser, CancellationToken.None);

        Assert.Equal(AiConfidence.FromAttempt(2), flow.Steps[0].AiAttribution!.Confidence);
        Assert.True(flow.Steps[0].AiAttribution!.Confidence < AiConfidence.FullConfidence);
    }

    private sealed class SequencedProvider : IAiProvider
    {
        private readonly Queue<string> _responses;
        public string Id => "seq-stub";
        public SequencedProvider(params string[] responses) => _responses = new Queue<string>(responses);

        public Task<ProviderResponse> GenerateStructuredAsync(StructuredGenerationRequest request, CancellationToken ct)
        {
            var raw = _responses.Dequeue();
            return Task.FromResult(new ProviderResponse(raw, System.Text.Json.Nodes.JsonNode.Parse(raw), 10, 10, TimeSpan.FromMilliseconds(5)));
        }
    }

    [Fact]
    public async Task Multi_step_saveAs_and_vars_chaining_resolves_correctly()
    {
        // The spec's own "hardest, most-weighted criterion": step 2's param references step 1's
        // saveAs binding, and Day 2's own publish validator (via Flow.Publish) proves it resolves.
        var (coder, provider) = BuildCoder();
        provider.SetFixture(Coder.Capability, """
            {"name":"Chained flow","declaredVariables":[],"steps":[
              {"name":"Login","module":"web","action":"navigate","params":{"url":"https://x/login"},"saveAs":"loginResult","when":null},
              {"name":"Check order","module":"api","action":"get","params":{"url":"{{vars.loginResult.orderUrl}}"},"saveAs":"order","when":null}
            ]}
            """);

        var flow = await coder.GenerateAsync(WebAndApiIntent, TestTenant, TestUser, CancellationToken.None);

        Assert.Equal(FlowStatus.Draft, flow.Status);
        Assert.Equal("loginResult", flow.Steps[0].SaveAs);
        Assert.Contains("{{vars.loginResult.orderUrl}}", flow.Steps[1].Params["url"]!.ToString());
    }

    [Fact]
    public async Task A_forward_reference_to_a_not_yet_produced_variable_fails_publish_validation()
    {
        // Flow.Publish's own "produced earlier" rule (D2-T5) — a step referencing a saveAs that
        // hasn't run yet is unresolved exactly like one that never runs at all. Proves Coder
        // really runs Flow.Publish rather than a looser check of its own.
        var (coder, provider) = BuildCoder();
        provider.SetFixture(Coder.Capability, """
            {"name":"Bad order flow","declaredVariables":[],"steps":[
              {"name":"Check order","module":"api","action":"get","params":{"url":"{{vars.loginResult.orderUrl}}"},"saveAs":"order","when":null},
              {"name":"Login","module":"web","action":"navigate","params":{"url":"https://x/login"},"saveAs":"loginResult","when":null}
            ]}
            """);

        var ex = await Assert.ThrowsAsync<AiGenerationFailedException>(
            () => coder.GenerateAsync(WebAndApiIntent, TestTenant, TestUser, CancellationToken.None));

        Assert.Contains(ex.ValidationErrors, e => e.Contains("loginResult") && e.Contains("unresolved_variable"));
    }

    [Fact]
    public async Task A_declared_variable_is_available_from_the_first_step()
    {
        var (coder, provider) = BuildCoder();
        provider.SetFixture(Coder.Capability, """
            {"name":"Uses a trigger-time variable","declaredVariables":["baseUrl"],"steps":[
              {"name":"Login","module":"web","action":"navigate","params":{"url":"{{vars.baseUrl}}/login"},"saveAs":null,"when":null}
            ]}
            """);

        var flow = await coder.GenerateAsync(WebAndApiIntent, TestTenant, TestUser, CancellationToken.None);

        Assert.Equal(FlowStatus.Draft, flow.Status);
        Assert.True(flow.DeclaredVariables.ContainsKey("baseUrl"));
    }

    [Fact]
    public async Task A_missing_required_param_fails_the_same_Day_2_publish_check_not_a_second_copy()
    {
        var (coder, provider) = BuildCoder();
        provider.SetFixture(Coder.Capability, """
            {"name":"Missing param flow","declaredVariables":[],"steps":[
              {"name":"Login","module":"web","action":"navigate","params":{},"saveAs":null,"when":null}
            ]}
            """);

        var ex = await Assert.ThrowsAsync<AiGenerationFailedException>(
            () => coder.GenerateAsync(WebAndApiIntent, TestTenant, TestUser, CancellationToken.None));

        Assert.Contains(ex.ValidationErrors, e => e.Contains("missing_required_param") && e.Contains("url"));
    }

    [Fact]
    public async Task A_step_using_a_module_outside_the_candidate_set_is_rejected()
    {
        var (coder, provider) = BuildCoder();
        provider.SetFixture(Coder.Capability, """
            {"name":"Out of scope module","declaredVariables":[],"steps":[
              {"name":"Check logs","module":"logs","action":"checkRules","params":{},"saveAs":null,"when":null}
            ]}
            """);

        var ex = await Assert.ThrowsAsync<AiGenerationFailedException>(
            () => coder.GenerateAsync(WebAndApiIntent, TestTenant, TestUser, CancellationToken.None));

        Assert.Contains(ex.ValidationErrors, e => e.Contains("logs") && e.Contains("not one of the candidate modules"));
    }

    [Fact]
    public async Task The_context_sent_to_the_provider_has_full_ActionSchema_for_candidates_only_never_the_whole_registry()
    {
        // §R2: Coder gets full ActionSchema (every ParamSpec), but strictly scoped to the
        // candidate modules in ParsedIntent — "logs" is registered but not a candidate, so it
        // must never appear in the context at all.
        var (coder, provider) = BuildCoder();
        provider.SetFixture(Coder.Capability, """
            {"name":"x","declaredVariables":[],"steps":[{"name":"s","module":"web","action":"navigate","params":{"url":"https://x"},"saveAs":null,"when":null}]}
            """);

        await coder.GenerateAsync(WebAndApiIntent, TestTenant, TestUser, CancellationToken.None);

        var sentPrompt = provider.LastRequest!.SystemPrompt;
        Assert.Contains("\"kind\":\"web\"", sentPrompt);
        Assert.Contains("\"kind\":\"api\"", sentPrompt);
        Assert.Contains("\"required\":true", sentPrompt); // full ParamSpec detail, unlike Analyst's stripped context
        Assert.DoesNotContain("\"kind\":\"logs\"", sentPrompt); // registered, but not a candidate — excluded
        Assert.DoesNotContain("checkRules", sentPrompt);
    }

    [Fact]
    public async Task The_assembled_prompt_stays_within_Coders_configured_MaxInputChars()
    {
        var provider = new Yukti.Infrastructure.InMemory.Ai.StubAiProvider();
        var registry = new ModuleRegistry();
        registry.Register(new FakeWebModule());
        registry.Register(new FakeApiModule());
        var resolver = new FakeModuleActionResolver()
            .Register(FakeWebModule.Kind, new ActionSchema { ActionName = "navigate", Parameters = new[] { new ParamSpec { Name = "url", Type = ParamType.String, Required = true } } });
        var coderCap = new AiCapabilityOptions(MaxInputChars: 12000, MaxOutputTokens: 2048, Temperature: 0);
        var engine = new AiPlanningEngine(provider, new FixedPromptLoader("Coder instructions.\n{{CONTEXT}}"),
            new NoOpCostRecorder(), new ConstantCapabilityOptions(coderCap), new AlwaysAllowedBudgetGuard(), Microsoft.Extensions.Logging.Abstractions.NullLogger<AiPlanningEngine>.Instance);
        var coder = new Coder(engine, registry, resolver);

        provider.SetFixture(Coder.Capability, """
            {"name":"x","declaredVariables":[],"steps":[{"name":"s","module":"web","action":"navigate","params":{"url":"https://x"},"saveAs":null,"when":null}]}
            """);

        await coder.GenerateAsync(WebAndApiIntent, TestTenant, TestUser, CancellationToken.None);

        var sentPrompt = provider.LastRequest!.SystemPrompt;
        Assert.True(sentPrompt.Length <= coderCap.MaxInputChars,
            $"assembled prompt was {sentPrompt.Length} chars, expected <= {coderCap.MaxInputChars}");
    }

    private sealed class ConstantCapabilityOptions : IAiCapabilityOptionsProvider
    {
        private readonly AiCapabilityOptions _options;
        public ConstantCapabilityOptions(AiCapabilityOptions options) => _options = options;
        public AiCapabilityOptions GetFor(string capability) => _options;
        public int MaxValidationRetries => 1;
    }
}
