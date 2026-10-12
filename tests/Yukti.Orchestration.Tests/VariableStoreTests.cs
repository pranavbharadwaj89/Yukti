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

/// <summary>Echoes its parameters back as StepOutcome.Data — the smallest
/// possible module for proving saveAs/interpolation wiring end-to-end
/// through the real FlowEngine, not just VariableStore in isolation.</summary>
public sealed class EchoModule : IAutomationModule
{
    public static readonly ModuleKind Kind = ModuleKind.Custom("echo-test-only");
    ModuleKind IAutomationModule.Kind => Kind;
    public string ContractVersion => "1.0.0";

    public IReadOnlyList<ActionSchema> GetSupportedActions() => new[]
    {
        new ActionSchema { ActionName = "echo", Description = "Echoes params back as Data.", Parameters = Array.Empty<ParamSpec>() },
    };

    public Task Setup(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;
    public Task Teardown(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;

    public Task<StepOutcome> Run(string action, IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct) =>
        Task.FromResult(StepOutcome.Passed(data: parameters));
}

/// <summary>
/// D2-T2 (P0-3 per the owner's "Yukti — Day 2 Spec" doc). No existing test
/// anywhere in the repo exercised VariableStore under any name before this
/// file — confirmed via `dotnet test --list-tests` and a repo-wide grep for
/// "VariableStore"/"saveAs" (plan/GAP-MATRIX.md, D1-T5). Genuinely new
/// coverage, not a filter-name fix.
/// </summary>
public sealed class VariableStoreTests
{
    // run.Variables is the flat bag keyed directly by saveAs name — the
    // literal "vars" token in a template is a path prefix the resolver
    // skips over (ResolvePath/TryResolvePath), not an actual key in this
    // dictionary. Confirmed against Yukti.Host/Program.cs's real usage
    // (SaveAs: "orgInfo", referenced later as {{vars.orgInfo.repos_url}}).
    private static readonly IReadOnlyDictionary<string, object?> Vars = new Dictionary<string, object?>
    {
        ["a"] = "top-level",
        ["orgInfo"] = new Dictionary<string, object?>
        {
            ["repos_url"] = "https://api.github.com/orgs/anthropics/repos",
            ["b"] = new Dictionary<string, object?> { ["c"] = "deeply-nested" },
            ["count"] = 42,
            ["legitimatelyNull"] = null,
        },
    };

    [Fact]
    public void A_top_level_reference_resolves()
    {
        var store = new VariableStore();
        var result = store.Interpolate(new Dictionary<string, object?> { ["x"] = "{{vars.a}}" }, Vars);
        Assert.Equal("top-level", result["x"]);
    }

    [Fact]
    public void A_nested_reference_resolves()
    {
        var store = new VariableStore();
        var result = store.Interpolate(new Dictionary<string, object?> { ["x"] = "{{vars.orgInfo.b.c}}" }, Vars);
        Assert.Equal("deeply-nested", result["x"]);
    }

    [Fact]
    public void A_reference_to_a_missing_path_fails_the_step_naming_the_path_never_as_an_empty_string()
    {
        var store = new VariableStore();

        var ex = Assert.Throws<VariableResolutionException>(() =>
            store.Interpolate(new Dictionary<string, object?> { ["x"] = "{{vars.orgInfo.doesNotExist}}" }, Vars));

        Assert.Equal("vars.orgInfo.doesNotExist", ex.Path);
        Assert.Contains("vars.orgInfo.doesNotExist", ex.Message);
    }

    [Fact]
    public void A_missing_path_embedded_in_a_larger_string_also_fails_rather_than_becoming_an_empty_string()
    {
        var store = new VariableStore();

        var ex = Assert.Throws<VariableResolutionException>(() =>
            store.Interpolate(new Dictionary<string, object?> { ["x"] = "prefix-{{vars.nope}}-suffix" }, Vars));

        Assert.Equal("vars.nope", ex.Path);
    }

    [Fact]
    public void A_parameter_with_no_template_passes_through_unchanged()
    {
        var store = new VariableStore();
        var result = store.Interpolate(new Dictionary<string, object?> { ["x"] = "plain string, no braces" }, Vars);
        Assert.Equal("plain string, no braces", result["x"]);
    }

    [Fact]
    public void A_whole_parameter_template_keeps_the_resolved_values_real_type_not_just_its_string_form()
    {
        var store = new VariableStore();
        var result = store.Interpolate(new Dictionary<string, object?> { ["x"] = "{{vars.orgInfo.count}}" }, Vars);
        Assert.Equal(42, result["x"]);
        Assert.IsType<int>(result["x"]);
    }

    [Fact]
    public void A_whole_parameter_template_resolving_to_an_object_keeps_its_object_type()
    {
        var store = new VariableStore();
        var result = store.Interpolate(new Dictionary<string, object?> { ["x"] = "{{vars.orgInfo.b}}" }, Vars);
        Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(result["x"]);
    }

    [Fact]
    public void A_path_that_resolves_to_a_genuinely_null_value_is_not_treated_as_missing()
    {
        // Distinct from "not found": a prior step legitimately saved null.
        // Only the *last* segment may be null; an *earlier* segment being
        // null (nothing to walk further into) is still "missing" for a
        // longer path, exercised separately above via doesNotExist.
        var store = new VariableStore();
        var result = store.Interpolate(new Dictionary<string, object?> { ["x"] = "{{vars.orgInfo.legitimatelyNull}}" }, Vars);
        Assert.Null(result["x"]);
    }

    [Fact]
    public async Task Two_concurrent_interpolations_against_different_variable_scopes_never_share_state()
    {
        var store = new VariableStore();
        var varsA = new Dictionary<string, object?> { ["x"] = "A" };
        var varsB = new Dictionary<string, object?> { ["x"] = "B" };

        var tasks = Enumerable.Range(0, 50).Select(i => Task.Run(() =>
        {
            var vars = i % 2 == 0 ? varsA : varsB;
            var expected = i % 2 == 0 ? "A" : "B";
            var result = store.Interpolate(new Dictionary<string, object?> { ["x"] = "{{vars.x}}" }, vars);
            Assert.Equal(expected, result["x"]);
        }));

        await Task.WhenAll(tasks);
    }

    private static FlowEngine BuildEngine()
    {
        var loggerFactory = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Warning));
        var dispatcher = new InMemoryDomainEventDispatcher();
        var uowFactory = new InMemoryUnitOfWorkFactory(dispatcher);
        var runRepo = new InMemoryFlowRunRepository(uowFactory);

        var registry = new ModuleRegistry();
        registry.Register(new EchoModule());
        var moduleRegistrations = new InMemoryModuleRegistrationRepository(uowFactory);
        var moduleDispatcher = new ModuleDispatcher(registry, moduleRegistrations, new ModuleExecutionStrategySelector(), loggerFactory.CreateLogger<ModuleDispatcher>());
        var variableStore = new VariableStore();
        var retryHandler = new RetryFlakeHandler(loggerFactory.CreateLogger<RetryFlakeHandler>());
        return new FlowEngine(moduleDispatcher, registry, variableStore, retryHandler, runRepo, uowFactory, loggerFactory.CreateLogger<FlowEngine>());
    }

    [Fact]
    public async Task A_saveAs_value_is_visible_to_later_steps_but_never_to_earlier_ones()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("saveAs ordering flow", null, tenantId, UserId.New());
        flow.SetContinueOnFailure(true); // step 1 deliberately references a var that won't exist until step 2

        flow.AddStep("Step 1 — references step 2's output too early", EchoModule.Kind, "echo",
            new Dictionary<string, object?> { ["tooEarly"] = "{{vars.fromStep2.value}}" }, saveAs: "fromStep1");
        flow.AddStep("Step 2 — produces the value", EchoModule.Kind, "echo",
            new Dictionary<string, object?> { ["value"] = "produced-by-step-2" }, saveAs: "fromStep2");
        flow.AddStep("Step 3 — consumes step 2's output", EchoModule.Kind, "echo",
            new Dictionary<string, object?> { ["fromStep2"] = "{{vars.fromStep2.value}}" });

        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);
        var completed = await engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);

        Assert.Equal(3, completed.Results.Count);
        Assert.Equal(StepStatus.Failed, completed.Results[0].Status); // step 1: fromStep2 didn't exist yet
        Assert.Equal(StepStatus.Passed, completed.Results[1].Status); // step 2: produces it
        Assert.Equal(StepStatus.Passed, completed.Results[2].Status); // step 3: sees step 2's value
        var step3Data = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(completed.Results[2].Data);
        Assert.Equal("produced-by-step-2", step3Data["fromStep2"]);
    }

    [Fact]
    public async Task An_unresolved_variable_fails_only_that_step_and_respects_fail_fast_by_default()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("Unresolved variable, fail-fast", null, tenantId, UserId.New());
        flow.AddStep("Step 1 — bad reference", EchoModule.Kind, "echo",
            new Dictionary<string, object?> { ["x"] = "{{vars.neverProduced.anything}}" });
        flow.AddStep("Step 2 — never reached", EchoModule.Kind, "echo", new Dictionary<string, object?>());

        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);
        var completed = await engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);

        var result = Assert.Single(completed.Results); // fail-fast: step 2 never dispatched
        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("vars.neverProduced.anything", result.Error);
        Assert.Equal(RunStatus.Failed, completed.Status);
    }
}
