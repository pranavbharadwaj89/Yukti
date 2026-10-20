using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yukti.Application.Ai;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.Ai;
using Yukti.Infrastructure.InMemory.Ai;
using Yukti.Infrastructure.InMemory.Modules;
using Yukti.Orchestration;

namespace Yukti.Infrastructure.Tests;

/// <summary>
/// ADR-0005: the e2e suite runs the real API with canned AI answers from
/// <c>apps/yukti-gui/e2e/fixtures/ai/*.json</c>. Those files are only useful while they pass the
/// real validators against the real modules — this runs the exact same files through the real
/// Analyst and Coder (real registry, real prompts, real <see cref="Flow.Publish"/> rules), so a
/// module schema change that breaks them fails here, fast and locally, instead of only in the
/// slower CI e2e job.
/// </summary>
public sealed class E2eFixtureContractTests
{
    private sealed class NoOpCostRecorder : IAiCostRecorder
    {
        public Task RecordAsync(AiCostRecordData record, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class AlwaysAllowedBudgetGuard : IAiBudgetGuard
    {
        public Task<AiBudgetCheck> CheckAsync(TenantId tenantId, string provider, int estimatedInputTokens, int maxOutputTokens, CancellationToken ct) =>
            Task.FromResult(new AiBudgetCheck(Allowed: true, NearCap: false, SpentUsd: 0m, ProjectedUsd: 0m, CapUsd: 1000m));
    }

    private sealed class RealModuleResolver : IModuleActionResolver
    {
        private readonly ModuleRegistry _registry;
        public RealModuleResolver(ModuleRegistry registry) => _registry = registry;
        public Task<ActionSchema?> TryGetActionSchema(ModuleKind module, string action, CancellationToken ct) =>
            Task.FromResult(_registry.Resolve(module)?.GetSupportedActions().FirstOrDefault(a => a.ActionName == action));
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Yukti.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Could not locate repo root (Yukti.sln).");
    }

    [Fact]
    public async Task The_e2e_fixtures_pass_the_real_Analyst_and_Coder_and_produce_a_chained_Draft()
    {
        var root = RepoRoot();
        var fixtures = Path.Combine(root, "apps", "yukti-gui", "e2e", "fixtures", "ai");
        var provider = new StubAiProvider();
        foreach (var file in Directory.GetFiles(fixtures, "*.json"))
            provider.SetFixture(Path.GetFileNameWithoutExtension(file), await File.ReadAllTextAsync(file));

        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(root, "src", "Yukti.Api", "appsettings.json"))
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ai:PromptsBasePath"] = Path.Combine(root, "Sarvagna", "Prompts") })
            .Build();
        var engine = new AiPlanningEngine(provider, new FilePromptLoader(config), new NoOpCostRecorder(),
            new ConfigurationAiCapabilityOptionsProvider(config), new AlwaysAllowedBudgetGuard(), NullLogger<AiPlanningEngine>.Instance);

        var registry = new ModuleRegistry();
        registry.Register(new ApiModule());
        registry.Register(new WebModule());
        registry.Register(new LogsModule());
        registry.Register(new MobileModule());

        var intent = await new Analyst(engine, registry).AnalyzeAsync("Check the Yukti API is up", TenantId.New(), CancellationToken.None);
        var flow = await new Coder(engine, registry, new RealModuleResolver(registry))
            .GenerateAsync(intent, TenantId.New(), new UserId(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(FlowStatus.Draft, flow.Status);
        Assert.Equal(2, flow.Steps.Count);
        Assert.Equal("health", flow.Steps[0].SaveAs);
        Assert.Contains("{{vars.health.body.service}}", flow.Steps[1].Params["url"]!.ToString());
        Assert.All(flow.Steps, s => Assert.NotNull(s.AiAttribution));
    }
}
