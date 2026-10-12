using Microsoft.Extensions.Logging;
using Xunit;
using Yukti.Application.Abstractions;
using Yukti.Contracts;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// Defined only here, never under src/ — D2-T1's whole point is that a
/// brand-new module needs zero other code changes to appear fully wired,
/// both in the in-process engine (<see cref="ModuleRegistry"/>) and in the
/// schema the real API's GET /api/v1/modules endpoint maps directly from
/// (Program.cs's ActionSchemaResponse.From(module.GetSupportedActions())).
/// </summary>
public sealed class SyntheticXyzModule : IAutomationModule
{
    public static readonly ModuleKind Kind = ModuleKind.Custom("synthetic-xyz");
    ModuleKind IAutomationModule.Kind => Kind;
    public string ContractVersion => "1.0.0";

    public IReadOnlyDictionary<string, object?>? LastParameters { get; private set; }

    public IReadOnlyList<ActionSchema> GetSupportedActions() => new[]
    {
        new ActionSchema
        {
            ActionName = "doSomethingNovel",
            Description = "A throwaway action proving a brand-new module needs zero core changes.",
            Parameters = new[]
            {
                new ParamSpec { Name = "targetId", Type = ParamType.String, Required = true, Description = "Id of the thing this action targets." },
                new ParamSpec { Name = "retryCount", Type = ParamType.Number, Required = false, DefaultValue = 3, Description = "How many times to retry internally before giving up." },
                new ParamSpec { Name = "payload", Type = ParamType.Object, Required = true, Description = "Arbitrary structured input for the action." },
            },
        },
    };

    public Task Setup(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;
    public Task Teardown(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;

    public Task<StepOutcome> Run(string action, IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct)
    {
        LastParameters = parameters;
        return Task.FromResult(StepOutcome.Passed("synthetic-xyz ran", parameters));
    }
}

public sealed class ModuleRegistryTests
{
    private static FlowEngine BuildEngine(IAutomationModule module, out ModuleRegistry registry)
    {
        var loggerFactory = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Warning));
        var dispatcher = new InMemoryDomainEventDispatcher();
        var uowFactory = new InMemoryUnitOfWorkFactory(dispatcher);
        var runRepo = new InMemoryFlowRunRepository(uowFactory);

        registry = new ModuleRegistry();
        registry.Register(module);
        var moduleRegistrations = new InMemoryModuleRegistrationRepository(uowFactory);
        var moduleDispatcher = new ModuleDispatcher(registry, moduleRegistrations, new ModuleExecutionStrategySelector(), loggerFactory.CreateLogger<ModuleDispatcher>());
        var variableStore = new VariableStore();
        var retryHandler = new RetryFlakeHandler(loggerFactory.CreateLogger<RetryFlakeHandler>());
        return new FlowEngine(moduleDispatcher, registry, variableStore, retryHandler, runRepo, uowFactory, loggerFactory.CreateLogger<FlowEngine>());
    }

    [Fact]
    public void A_brand_new_module_needs_zero_other_code_changes_to_appear_in_the_registry()
    {
        var module = new SyntheticXyzModule();
        BuildEngine(module, out var registry);

        var resolved = registry.Resolve(SyntheticXyzModule.Kind);

        Assert.Same(module, resolved);
        Assert.Contains(registry.All, m => m.Kind == SyntheticXyzModule.Kind);
    }

    [Fact]
    public void A_synthetic_modules_actions_carry_full_schema_with_every_description_populated()
    {
        var action = Assert.Single(new SyntheticXyzModule().GetSupportedActions());

        Assert.Equal("doSomethingNovel", action.ActionName);
        Assert.False(string.IsNullOrWhiteSpace(action.Description));
        Assert.Equal(3, action.Parameters.Count);

        var targetId = Assert.Single(action.Parameters, p => p.Name == "targetId");
        Assert.Equal(ParamType.String, targetId.Type);
        Assert.True(targetId.Required);
        Assert.False(string.IsNullOrWhiteSpace(targetId.Description));

        var retryCount = Assert.Single(action.Parameters, p => p.Name == "retryCount");
        Assert.Equal(ParamType.Number, retryCount.Type);
        Assert.False(retryCount.Required);
        Assert.Equal(3, retryCount.DefaultValue);
        Assert.False(string.IsNullOrWhiteSpace(retryCount.Description));

        var payload = Assert.Single(action.Parameters, p => p.Name == "payload");
        Assert.Equal(ParamType.Object, payload.Type);
        Assert.True(payload.Required);
        Assert.False(string.IsNullOrWhiteSpace(payload.Description));
    }

    [Fact]
    public async Task A_flow_step_on_the_synthetic_module_dispatches_to_its_Run_with_interpolated_parameters()
    {
        var module = new SyntheticXyzModule();
        var engine = BuildEngine(module, out _);

        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("Synthetic module smoke flow", null, tenantId, UserId.New());
        flow.AddStep("Do the novel thing", SyntheticXyzModule.Kind, "doSomethingNovel", new Dictionary<string, object?>
        {
            ["targetId"] = "abc-123",
            ["payload"] = new Dictionary<string, object?> { ["key"] = "value" },
        });
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);

        var completed = await engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);

        Assert.Equal(RunStatus.Passed, completed.Status);
        var result = Assert.Single(completed.Results);
        Assert.Equal(StepStatus.Passed, result.Status);
        Assert.NotNull(module.LastParameters);
        Assert.Equal("abc-123", module.LastParameters!["targetId"]);
    }

    [Fact]
    public void No_production_source_file_contains_the_word_synthetic()
    {
        // The negative half of D2-T1: if this module's definition — or any
        // wiring for it — ever leaks into src/, the "zero other code
        // changes" claim this whole test class exists to prove is false.
        var srcDir = Path.Combine(FindRepoRoot(), "src");

        var offendingFiles = Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("synthetic", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(offendingFiles);
    }

    [Fact]
    public void Registering_an_action_with_a_missing_description_fails_registration_with_a_clear_error()
    {
        var registration = new ModuleRegistration(ModuleKind.Custom("desc-less"), "No Description Module", TrustTier.Community, "1.0.0", null);
        var schemaWithNoDescription = new ActionSchema
        {
            ActionName = "doThing",
            Description = null,
            Parameters = Array.Empty<ParamSpec>(),
        };

        var ex = Assert.Throws<DomainException>(() => registration.RegisterAction(schemaWithNoDescription));
        Assert.Contains("doThing", ex.Message);
        Assert.Contains("Description", ex.Message);
    }

    [Fact]
    public void Registering_an_action_whose_parameter_has_a_missing_description_fails_registration_with_a_clear_error()
    {
        var registration = new ModuleRegistration(ModuleKind.Custom("desc-less-param"), "No Param Description Module", TrustTier.Community, "1.0.0", null);
        var schemaWithNoParamDescription = new ActionSchema
        {
            ActionName = "doThing",
            Description = "Does a thing.",
            Parameters = new[] { new ParamSpec { Name = "x", Type = ParamType.String, Required = true, Description = null } },
        };

        var ex = Assert.Throws<DomainException>(() => registration.RegisterAction(schemaWithNoParamDescription));
        Assert.Contains("'x'", ex.Message);
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Yukti.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Could not locate repo root (Yukti.sln) from " + AppContext.BaseDirectory);
    }
}
