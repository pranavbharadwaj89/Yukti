using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yukti.Application.Ai;
using Yukti.Infrastructure.Ai;
using Yukti.Infrastructure.InMemory.Ai;
using Yukti.Infrastructure.InMemory.Modules;
using Yukti.Orchestration;

namespace Yukti.Infrastructure.Tests;

/// <summary>
/// Token-economy spec §R2/§R4, against the real thing rather than test fakes: the real built-in
/// modules, the real versioned prompt files in <c>Sarvagna/Prompts</c>, and the real caps from
/// <c>src/Yukti.Api/appsettings.json</c>. If a module gains actions or a prompt grows past its
/// capability's configured <c>MaxInputChars</c>, every real generation would be rejected by the
/// guardrail — this fails first, loudly, instead of the feature silently dying in production.
/// </summary>
public sealed class RealRegistryPromptBudgetTests
{
    private sealed class NoOpCostRecorder : IAiCostRecorder
    {
        public Task RecordAsync(AiCostRecordData record, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class AlwaysAllowedBudgetGuard : IAiBudgetGuard
    {
        public Task<AiBudgetCheck> CheckAsync(Domain.SharedKernel.TenantId tenantId, string provider, int estimatedInputTokens, int maxOutputTokens, CancellationToken ct) =>
            Task.FromResult(new AiBudgetCheck(Allowed: true, NearCap: false, SpentUsd: 0m, ProjectedUsd: 0m, CapUsd: 1000m));
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Yukti.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Could not locate repo root (Yukti.sln).");
    }

    private static ModuleRegistry RealRegistry()
    {
        var registry = new ModuleRegistry();
        registry.Register(new ApiModule());
        registry.Register(new WebModule());
        registry.Register(new LogsModule());
        registry.Register(new MobileModule());
        return registry;
    }

    private static (AiPlanningEngine Engine, StubAiProvider Provider, IAiCapabilityOptionsProvider Caps) RealEngine()
    {
        var root = RepoRoot();
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(root, "src", "Yukti.Api", "appsettings.json"))
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ai:PromptsBasePath"] = Path.Combine(root, "Sarvagna", "Prompts") })
            .Build();
        var caps = new ConfigurationAiCapabilityOptionsProvider(config);
        var provider = new StubAiProvider();
        var engine = new AiPlanningEngine(provider, new FilePromptLoader(config), new NoOpCostRecorder(), caps,
            new AlwaysAllowedBudgetGuard(), NullLogger<AiPlanningEngine>.Instance);
        return (engine, provider, caps);
    }

    [Fact]
    public async Task Analyst_with_the_whole_real_registry_and_a_max_length_description_fits_its_configured_cap()
    {
        var (engine, provider, caps) = RealEngine();
        provider.SetFixture(Analyst.Capability, """{"candidateModuleKinds":["api"],"assertions":[],"ambiguities":[]}""");
        var description = new string('a', AiGuardrailPreCheck.MaxDescriptionLength);

        await new Analyst(engine, RealRegistry()).AnalyzeAsync(description, Domain.SharedKernel.TenantId.New(), CancellationToken.None);

        var cap = caps.GetFor(Analyst.Capability).MaxInputChars;
        var assembled = provider.LastRequest!.SystemPrompt.Length + provider.LastRequest.UserContent.Length;
        Assert.True(assembled <= cap, $"Analyst's real assembled prompt is {assembled} chars, over its configured cap of {cap}.");
    }

    [Fact]
    public async Task Coder_with_every_real_module_as_a_candidate_fits_its_configured_cap()
    {
        var (engine, provider, caps) = RealEngine();
        provider.SetFixture(Coder.Capability, """
            {"name":"x","declaredVariables":[],"steps":[{"name":"s","module":"api","action":"request","params":{"url":"https://x"},"saveAs":null,"when":null}]}
            """);
        var resolver = new CoderTestResolver(RealRegistry());
        var everyModule = new ParsedIntent(["api", "web", "logs", "mobile"], ["a check"], []);

        await new Coder(engine, RealRegistry(), resolver).GenerateAsync(
            everyModule, Domain.SharedKernel.TenantId.New(), new Domain.SharedKernel.UserId(Guid.NewGuid()), CancellationToken.None);

        var cap = caps.GetFor(Coder.Capability).MaxInputChars;
        var assembled = provider.LastRequest!.SystemPrompt.Length + provider.LastRequest.UserContent.Length;
        Assert.True(assembled <= cap, $"Coder's real assembled prompt (all modules as candidates) is {assembled} chars, over its configured cap of {cap}.");
    }

    /// <summary>Resolves actions straight from the real modules' own schemas.</summary>
    private sealed class CoderTestResolver : Domain.FlowAuthoring.IModuleActionResolver
    {
        private readonly ModuleRegistry _registry;
        public CoderTestResolver(ModuleRegistry registry) => _registry = registry;

        public Task<Domain.ModulePlugin.ActionSchema?> TryGetActionSchema(Domain.ModulePlugin.ModuleKind module, string action, CancellationToken ct) =>
            Task.FromResult(_registry.Resolve(module)?.GetSupportedActions().FirstOrDefault(a => a.ActionName == action));
    }
}
