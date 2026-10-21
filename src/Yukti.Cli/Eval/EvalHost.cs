using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Yukti.Application.Abstractions;
using Yukti.Application.Ai;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure;
using Yukti.Infrastructure.Ai;
using Yukti.Infrastructure.InMemory;
using Yukti.Infrastructure.InMemory.Ai;
using Yukti.Infrastructure.InMemory.Modules;
using Yukti.Orchestration;

namespace Yukti.Cli.Eval;

/// <summary>
/// D5-T8's composition root — deliberately not <see cref="CliHost"/> (the <c>run</c> command's
/// pure in-memory host): a real eval run needs the SAME, real, EF-backed <see
/// cref="IAiCostRepository"/>/<see cref="IAiUsageQuery"/>/<see cref="IAiBudgetGuard"/> the API uses
/// (one source of truth for AI spend — a second, parallel in-memory ledger for just this command
/// would make the budget cap meaningless), under one fixed, well-known tenant so spend actually
/// accumulates across runs instead of starting fresh every invocation like <c>run</c>'s own
/// per-call <c>TenantId.New()</c> does. <c>--dry-run</c> (§R9) never reaches this far — it only
/// ever needs <see cref="EstimateConfig"/>, which touches neither Postgres nor Gemini.
///
/// <c>Ai:Provider=fixture</c> (the same switch the API's Program.cs already defines, ADR-0005)
/// swaps in <see cref="StubAiProvider"/> + in-memory cost tracking instead, with no Postgres
/// dependency at all — this is how <c>EvalFlowGenerationTests</c> exercises early-stop and the
/// reuse-without-<c>--force</c> path (token-economy spec §R7: zero live calls, zero DB, in tests).
/// </summary>
public sealed class EvalHost : IDisposable
{
    /// <summary>Fixed, not random — the whole point of persisting cost rows under one tenant is
    /// that repeated eval runs accumulate against the same week-to-date spend the budget guard and
    /// <c>GET /ai/usage</c> both read, the same way a real tenant's spend would.</summary>
    public static readonly TenantId EvalTenantId = new(Guid.Parse("eeeeeeee-0000-0000-0000-000000000001"));

    public required IAnalyst Analyst { get; init; }
    public required ICoder Coder { get; init; }
    public required IAiBudgetGuard BudgetGuard { get; init; }
    public required IAiPricingProvider Pricing { get; init; }
    public required IAiCapabilityOptionsProvider CapabilityOptions { get; init; }
    public required ModuleRegistry Registry { get; init; }
    public required string ProviderId { get; init; }
    private YuktiDbContext? _dbContext;

    public static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Yukti.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Could not locate repo root (Yukti.sln) from " + AppContext.BaseDirectory);
    }

    private static IConfiguration BuildConfig(string repoRoot) => new ConfigurationBuilder()
        .AddJsonFile(Path.Combine(repoRoot, "src", "Yukti.Api", "appsettings.json"), optional: true)
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Ai:PromptsBasePath"] = Path.Combine(repoRoot, "Sarvagna", "Prompts") })
        .AddUserSecrets("6b746e6b-ec5a-4995-a73e-b26cd66270e2") // Yukti.Api's UserSecretsId — same local secrets store
        .AddEnvironmentVariables()
        .Build();

    /// <summary>Public so the CLI can compute a registry hash and check for a reusable stored
    /// result (§R9) without needing the rest of <see cref="Build"/> — a cache hit must cost
    /// nothing, not even a DB connection.</summary>
    public static ModuleRegistry BuildRegistry()
    {
        var registry = new ModuleRegistry();
        registry.Register(new ApiModule());
        registry.Register(new WebModule());
        registry.Register(new LogsModule());
        registry.Register(new MobileModule());
        return registry;
    }

    /// <summary>Everything <c>--dry-run</c> needs and nothing more — no provider HTTP client, no
    /// DB connection, so the cost estimate works even with Postgres down and no Gemini key set.</summary>
    public sealed record EstimateConfig(IAiCapabilityOptionsProvider CapabilityOptions, IAiPricingProvider Pricing, string ProviderId, ModuleRegistry Registry);

    public static EstimateConfig BuildForEstimate()
    {
        var config = BuildConfig(RepoRoot());
        var useFixture = string.Equals(config["Ai:Provider"], "fixture", StringComparison.OrdinalIgnoreCase);
        return new EstimateConfig(
            new ConfigurationAiCapabilityOptionsProvider(config),
            new ConfigurationAiPricingProvider(config),
            useFixture ? "stub" : "gemini",
            BuildRegistry());
    }

    public static async Task<EvalHost> Build(ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var repoRoot = RepoRoot();
        var config = BuildConfig(repoRoot);

        var registry = BuildRegistry();
        var resolver = new RegistryActionResolver(registry);

        var capabilityOptions = new ConfigurationAiCapabilityOptionsProvider(config);
        var pricing = new ConfigurationAiPricingProvider(config);
        var promptLoader = new FilePromptLoader(config);

        var useFixture = string.Equals(config["Ai:Provider"], "fixture", StringComparison.OrdinalIgnoreCase);
        IAiProvider provider;
        IAiCostRepository costRepository;
        IAiUsageQuery usageQuery;
        IAiRequestQuotaLedger quotaLedger;
        YuktiDbContext? dbContext = null;

        if (useFixture)
        {
            var fixtureDirectory = config["Ai:FixtureDirectory"]
                ?? throw new InvalidOperationException("Ai:Provider=fixture requires Ai:FixtureDirectory.");
            var stub = new StubAiProvider();
            foreach (var file in Directory.GetFiles(fixtureDirectory, "*.json"))
                stub.SetFixture(Path.GetFileNameWithoutExtension(file), File.ReadAllText(file));
            provider = stub;
            var inMemoryRepo = new InMemoryAiCostRepository();
            costRepository = inMemoryRepo;
            usageQuery = new InMemoryAiUsageQuery(inMemoryRepo);
            // §R7: fixture mode never touches the real, shared quota ledger either.
            quotaLedger = new InMemoryAiRequestQuotaLedger();
        }
        else
        {
            provider = new GeminiProvider(new EnvironmentCredentialResolver(), config, loggerFactory.CreateLogger<GeminiProvider>());
            var connectionString = config.GetConnectionString("Yukti")
                ?? throw new InvalidOperationException(
                    "Missing ConnectionStrings:Yukti. A real eval run needs the real Postgres so spend accumulates " +
                    "in the same ai_cost_records table the API and GET /ai/usage read. Use --dry-run (no connection " +
                    "needed) or Ai__Provider=fixture (stub, in-memory) if that's not available right now.");
            var options = new DbContextOptionsBuilder<YuktiDbContext>().UseNpgsql(connectionString).Options;
            dbContext = new YuktiDbContext(options);
            // RLS (CLAUDE.md §9): ai_cost_records is FORCE ROW LEVEL SECURITY — without this, every
            // query below sees zero rows for EvalTenantId, silently making the budget guard think
            // nothing has ever been spent. Same mechanism the API's own request pipeline uses.
            await new EfTenantSessionInitializer(dbContext).EstablishTenantContext(EvalTenantId, ct);
            costRepository = new EfAiCostRepository(dbContext);
            usageQuery = new EfAiUsageQuery(dbContext);
            // DISC-14: the same shared, on-disk ledger the real API and Sarvaghna's own Node
            // commit/push-hook calls write to — a real eval run must see (and count against) the
            // exact same daily request quota they do.
            quotaLedger = new FileAiRequestQuotaLedger(config);
        }

        var budgetOptions = new ConfigurationAiBudgetOptionsProvider(config);
        var budgetGuard = new AiBudgetGuard(usageQuery, pricing, budgetOptions, quotaLedger);
        var costRecorder = new AiCostRecorder(costRepository, pricing, quotaLedger);
        var engine = new AiPlanningEngine(provider, promptLoader, costRecorder, capabilityOptions, budgetGuard,
            loggerFactory.CreateLogger<AiPlanningEngine>());

        return new EvalHost
        {
            Analyst = new Yukti.Infrastructure.Ai.Analyst(engine, registry),
            Coder = new Yukti.Infrastructure.Ai.Coder(engine, registry, resolver),
            BudgetGuard = budgetGuard,
            Pricing = pricing,
            CapabilityOptions = capabilityOptions,
            Registry = registry,
            ProviderId = provider.Id,
            _dbContext = dbContext,
        };
    }

    public void Dispose() => _dbContext?.Dispose();

    /// <summary>Resolves straight off the real, in-process registry — same pattern
    /// <c>RealRegistryPromptBudgetTests.CoderTestResolver</c> already uses; no module_registrations
    /// table involved, since the eval harness never persists a Flow.</summary>
    private sealed class RegistryActionResolver : IModuleActionResolver
    {
        private readonly ModuleRegistry _registry;
        public RegistryActionResolver(ModuleRegistry registry) => _registry = registry;

        public Task<ActionSchema?> TryGetActionSchema(ModuleKind module, string action, CancellationToken ct) =>
            Task.FromResult(_registry.Resolve(module)?.GetSupportedActions().FirstOrDefault(a => a.ActionName == action));
    }
}
