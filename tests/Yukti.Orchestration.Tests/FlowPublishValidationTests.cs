using Xunit;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;

namespace Yukti.Orchestration.Tests;

/// <summary>Test-only double for the same IModuleActionResolver port both
/// the real API (EfModuleActionResolver) and the CLI will call — D2-T5's
/// own requirement that the validator is one shared component, never a
/// copy, is why Flow.Publish only ever depends on this interface.</summary>
public sealed class FakeModuleActionResolver : IModuleActionResolver
{
    private readonly Dictionary<(ModuleKind Module, string Action), ActionSchema> _actions = new();

    public FakeModuleActionResolver Register(ModuleKind module, ActionSchema schema)
    {
        _actions[(module, schema.ActionName)] = schema;
        return this;
    }

    public Task<ActionSchema?> TryGetActionSchema(ModuleKind module, string action, CancellationToken ct) =>
        Task.FromResult(_actions.TryGetValue((module, action), out var schema) ? schema : null);
}

/// <summary>D2-T5 (P0-2 per the owner's "Yukti — Day 2 Spec" doc). Much of
/// Flow.Publish's shape already existed (resolver pattern, ordering,
/// Draft-only-editable, CreateNewVersion) — this file proves it, and adds
/// what was genuinely missing: structured {field, code} errors (previously
/// plain strings), the missing_required_param check (didn't exist at all),
/// and a properly async IModuleActionResolver (the prior synchronous shape
/// forced both real implementations to block on the repository's async
/// call, caught by SARVAGHNA while building this very file).</summary>
public sealed class FlowPublishValidationTests
{
    private static readonly ModuleKind TestModule = ModuleKind.Custom("publish-test-only");

    private static ActionSchema ActionWithOneRequiredParam() => new()
    {
        ActionName = "doThing",
        Description = "Test action with one required param.",
        Parameters = new[] { new ParamSpec { Name = "targetId", Type = ParamType.String, Required = true, Description = "d" } },
    };

    private static Flow NewDraftFlow() =>
        Flow.CreateDraft("Publish validation test flow", null, TenantId.New(), UserId.New());

    [Fact]
    public async Task An_unregistered_action_fails_publish_naming_the_step_and_action()
    {
        var flow = NewDraftFlow();
        flow.AddStep("Step 1", TestModule, "doesNotExist", new Dictionary<string, object?>());
        var resolver = new FakeModuleActionResolver(); // nothing registered

        var result = await flow.Publish(resolver, CancellationToken.None);

        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Errors);
        Assert.Equal("steps[0].action", error.Field);
        Assert.Equal("unresolved_module_action", error.Code);
        Assert.Contains("doesNotExist", error.Message);
    }

    [Fact]
    public async Task A_variable_referenced_before_any_step_produces_it_fails_as_unresolved()
    {
        var flow = NewDraftFlow();
        flow.AddStep("Step 1", TestModule, "doThing", new Dictionary<string, object?> { ["targetId"] = "{{vars.never.produced}}" });
        var resolver = new FakeModuleActionResolver().Register(TestModule, ActionWithOneRequiredParam());

        var result = await flow.Publish(resolver, CancellationToken.None);

        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Errors, e => e.Code == "unresolved_variable");
        Assert.Equal("steps[0].params.targetId", error.Field);
    }

    [Fact]
    public async Task A_saveAs_from_a_later_step_counts_as_unresolved_too()
    {
        var flow = NewDraftFlow();
        flow.AddStep("Step 1 — references step 2's output too early", TestModule, "doThing",
            new Dictionary<string, object?> { ["targetId"] = "{{vars.laterStep.val}}" });
        flow.AddStep("Step 2 — produces it, but too late", TestModule, "doThing",
            new Dictionary<string, object?> { ["targetId"] = "x" }, saveAs: "laterStep");
        var resolver = new FakeModuleActionResolver().Register(TestModule, ActionWithOneRequiredParam());

        var result = await flow.Publish(resolver, CancellationToken.None);

        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Errors, e => e.Code == "unresolved_variable");
        Assert.Equal("steps[0].params.targetId", error.Field); // step 0, not step 1 — the producer is irrelevant, only order matters
    }

    [Fact]
    public async Task A_missing_required_parameter_fails_publish()
    {
        var flow = NewDraftFlow();
        flow.AddStep("Step 1", TestModule, "doThing", new Dictionary<string, object?>()); // targetId omitted
        var resolver = new FakeModuleActionResolver().Register(TestModule, ActionWithOneRequiredParam());

        var result = await flow.Publish(resolver, CancellationToken.None);

        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Errors);
        Assert.Equal("steps[0].params.targetId", error.Field);
        Assert.Equal("missing_required_param", error.Code);
    }

    [Fact]
    public async Task All_of_a_flows_errors_are_reported_in_one_response_not_just_the_first()
    {
        var flow = NewDraftFlow();
        flow.AddStep("Step 1 — bad action", TestModule, "doesNotExist", new Dictionary<string, object?>());
        flow.AddStep("Step 2 — missing required param", TestModule, "doThing", new Dictionary<string, object?>());
        var resolver = new FakeModuleActionResolver().Register(TestModule, ActionWithOneRequiredParam());

        var result = await flow.Publish(resolver, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.Code == "unresolved_module_action");
        Assert.Contains(result.Errors, e => e.Code == "missing_required_param");
    }

    [Fact]
    public async Task A_failed_publish_leaves_the_flow_as_Draft_with_no_changes()
    {
        var flow = NewDraftFlow();
        flow.AddStep("Step 1", TestModule, "doesNotExist", new Dictionary<string, object?>());
        var resolver = new FakeModuleActionResolver();

        var result = await flow.Publish(resolver, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(FlowStatus.Draft, flow.Status);
        Assert.Null(flow.PublishedAt);
    }

    [Fact]
    public async Task A_successful_publish_sets_Published_and_records_PublishedAt()
    {
        var flow = NewDraftFlow();
        flow.AddStep("Step 1", TestModule, "doThing", new Dictionary<string, object?> { ["targetId"] = "x" });
        var resolver = new FakeModuleActionResolver().Register(TestModule, ActionWithOneRequiredParam());
        var before = DateTimeOffset.UtcNow;

        var result = await flow.Publish(resolver, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Errors);
        Assert.Equal(FlowStatus.Published, flow.Status);
        Assert.NotNull(flow.PublishedAt);
        Assert.True(flow.PublishedAt >= before);
    }

    [Fact]
    public async Task CreateNewVersion_shares_FamilyId_increments_Version_and_never_touches_the_original()
    {
        var flow = NewDraftFlow();
        flow.AddStep("Step 1", TestModule, "doThing", new Dictionary<string, object?> { ["targetId"] = "x" });
        var resolver = new FakeModuleActionResolver().Register(TestModule, ActionWithOneRequiredParam());
        await flow.Publish(resolver, CancellationToken.None);
        var publishedAtV1 = flow.PublishedAt;
        var v1Id = flow.Id;

        var v2 = flow.CreateNewVersion(UserId.New());

        Assert.Equal(FlowStatus.Draft, v2.Status);
        Assert.Equal(flow.FamilyId, v2.FamilyId);
        Assert.Equal(flow.Version + 1, v2.Version);
        Assert.NotEqual(v1Id, v2.Id);
        Assert.Single(v2.Steps); // steps carried over

        // The original is completely untouched by creating v2.
        Assert.Equal(FlowStatus.Published, flow.Status);
        Assert.Equal(publishedAtV1, flow.PublishedAt);
        Assert.Equal(v1Id, flow.Id);
    }

    [Fact]
    public async Task Attempting_to_edit_a_Published_flow_directly_throws_rather_than_silently_changing_it()
    {
        var flow = NewDraftFlow();
        flow.AddStep("Step 1", TestModule, "doThing", new Dictionary<string, object?> { ["targetId"] = "x" });
        var resolver = new FakeModuleActionResolver().Register(TestModule, ActionWithOneRequiredParam());
        await flow.Publish(resolver, CancellationToken.None);

        Assert.Throws<DomainException>(() =>
            flow.AddStep("Sneaked in", TestModule, "doThing", new Dictionary<string, object?> { ["targetId"] = "y" }));
        Assert.Single(flow.Steps); // the throw is real — no step was appended before it
    }

    [Fact]
    public async Task A_run_points_to_the_exact_published_version_not_the_family_and_publishing_v2_never_touches_v1()
    {
        var v1 = NewDraftFlow();
        v1.AddStep("Step 1", TestModule, "doThing", new Dictionary<string, object?> { ["targetId"] = "x" });
        var resolver = new FakeModuleActionResolver().Register(TestModule, ActionWithOneRequiredParam());
        await v1.Publish(resolver, CancellationToken.None);

        var run = FlowRun.Create(v1.Id, RunTrigger.Api, v1.TenantId);
        Assert.Equal(v1.Id, run.FlowId);

        var v2 = v1.CreateNewVersion(UserId.New());
        await v2.Publish(resolver, CancellationToken.None);

        Assert.Equal(v1.Id, run.FlowId); // unaffected by v2 existing or publishing
        Assert.Equal(FlowStatus.Published, v1.Status); // v1 still published, unchanged
        Assert.NotEqual(v1.Id, v2.Id);
        Assert.Equal(v1.FamilyId, v2.FamilyId);
    }
}
