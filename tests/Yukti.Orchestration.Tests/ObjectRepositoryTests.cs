using Microsoft.Extensions.Logging;
using Xunit;
using Yukti.Application.Abstractions;
using Yukti.Application.UiVocabulary;
using Yukti.Contracts;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.SharedKernel;
using Yukti.Domain.UiVocabulary;
using Yukti.Infrastructure.InMemory;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// Web UI Testing Module Spec, WEB-T2: the Object Repository artifact itself (this file's first
/// half) and the run-time resolution mechanism that makes "editing an entry affects every flow
/// referencing it with zero flow edits" literally true (second half) — same shape as
/// RequestTemplateResolutionTests, using the same EchoModule to inspect exactly what a step
/// received after resolution.
/// </summary>
public sealed class ObjectRepositoryTests
{
    private static Locator MakeLocator(string value, string? description = "a locator") =>
        new(new LocatorSpec(LocatorStrategy.Css, value, null), Array.Empty<LocatorSpec>(), description, null, null);

    [Fact]
    public void A_new_repository_starts_as_an_empty_Draft()
    {
        var repository = ObjectRepository.Create("Checkout", TenantId.New());

        Assert.Equal(RepositoryStatus.Draft, repository.Status);
        Assert.Empty(repository.Pages);
        Assert.Equal(1, repository.Version);
    }

    [Fact]
    public void SetElement_adds_a_new_page_and_increments_version()
    {
        var repository = ObjectRepository.Create("Checkout", TenantId.New());

        repository.SetElement("Checkout", "submitButton", MakeLocator("button.submit"));

        var page = Assert.Single(repository.Pages);
        Assert.Equal("Checkout", page.Name);
        Assert.Equal("button.submit", page.Elements["submitButton"].Primary.Value);
        Assert.Equal(2, repository.Version);
    }

    [Fact]
    public void SetElement_on_an_existing_page_adds_the_element_without_disturbing_siblings()
    {
        var repository = ObjectRepository.Create("Checkout", TenantId.New());
        repository.SetElement("Checkout", "submitButton", MakeLocator("button.submit"));

        repository.SetElement("Checkout", "cancelButton", MakeLocator("button.cancel"));

        var page = Assert.Single(repository.Pages);
        Assert.Equal(2, page.Elements.Count);
        Assert.Equal("button.submit", page.Elements["submitButton"].Primary.Value);
        Assert.Equal("button.cancel", page.Elements["cancelButton"].Primary.Value);
    }

    [Fact]
    public void Publish_fails_naming_every_entry_with_no_description()
    {
        var repository = ObjectRepository.Create("Checkout", TenantId.New());
        repository.SetElement("Checkout", "submitButton", MakeLocator("button.submit", description: null));
        repository.SetElement("Checkout", "cancelButton", MakeLocator("button.cancel", description: ""));
        repository.SetElement("Checkout", "okButton", MakeLocator("button.ok", description: "the OK button"));

        var result = repository.Publish();

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Field == "pages.Checkout.submitButton");
        Assert.Contains(result.Errors, e => e.Field == "pages.Checkout.cancelButton");
        Assert.DoesNotContain(result.Errors, e => e.Field == "pages.Checkout.okButton");
        Assert.Equal(RepositoryStatus.Draft, repository.Status); // unchanged on failure
    }

    [Fact]
    public void Publish_succeeds_when_every_entry_has_a_description()
    {
        var repository = ObjectRepository.Create("Checkout", TenantId.New());
        repository.SetElement("Checkout", "submitButton", MakeLocator("button.submit", description: "the submit button"));

        var result = repository.Publish();

        Assert.True(result.Succeeded);
        Assert.Equal(RepositoryStatus.Published, repository.Status);
    }

    [Fact]
    public void Editing_an_element_after_publish_is_allowed_and_does_not_revert_status()
    {
        var repository = ObjectRepository.Create("Checkout", TenantId.New());
        repository.SetElement("Checkout", "submitButton", MakeLocator("button.submit", description: "the submit button"));
        repository.Publish();

        repository.SetElement("Checkout", "submitButton", MakeLocator("button.submit-v2", description: "the submit button"));

        Assert.Equal(RepositoryStatus.Published, repository.Status);
        Assert.Equal("button.submit-v2", repository.FindElement("Checkout", "submitButton")!.Primary.Value);
    }

    // ── Run-time resolution (FlowEngine.ResolveLocatorRef) ──

    private static (FlowEngine Engine, InMemoryObjectRepositoryRepository Repositories) BuildEngine()
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
        var repositories = new InMemoryObjectRepositoryRepository(uowFactory);
        var elements = new InMemoryObjectRepositoryElementQuery(repositories);
        var engine = new FlowEngine(moduleDispatcher, registry, variableStore, retryHandler, runRepo, uowFactory,
            loggerFactory.CreateLogger<FlowEngine>(), repositoryElements: elements);
        return (engine, repositories);
    }

    private static Dictionary<string, object?> RepositoryRefParam(string page, string element) =>
        new() { ["repositoryPage"] = page, ["repositoryElement"] = element };

    private static async Task<IReadOnlyDictionary<string, object?>> RunOneStepAndGetEchoedParams(
        FlowEngine engine, TenantId tenantId, Dictionary<string, object?> stepParams)
    {
        var flow = Flow.CreateDraft("Repository resolution flow", null, tenantId, UserId.New());
        flow.AddStep("Step", EchoModule.Kind, "echo", stepParams);
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);
        var completed = await engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);
        var result = Assert.Single(completed.Results);
        Assert.Equal(StepStatus.Passed, result.Status);
        return Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(result.Data);
    }

    [Fact]
    public async Task A_step_referencing_a_repository_entry_receives_the_current_Locator()
    {
        var (engine, repositories) = BuildEngine();
        var tenantId = TenantId.New();
        var repository = ObjectRepository.Create("Checkout", tenantId);
        repository.SetElement("Checkout", "submitButton", MakeLocator("button.submit", description: "the submit button"));
        await repositories.Save(repository, CancellationToken.None);

        var echoed = await RunOneStepAndGetEchoedParams(engine, tenantId, new Dictionary<string, object?>
        {
            ["selector"] = RepositoryRefParam("Checkout", "submitButton"),
        });

        var locator = Assert.IsType<Locator>(echoed["selector"]);
        Assert.Equal("button.submit", locator.Primary.Value);
    }

    [Fact]
    public async Task Three_flows_pick_up_a_renamed_locator_with_zero_flow_edits()
    {
        var (engine, repositories) = BuildEngine();
        var tenantId = TenantId.New();
        var repository = ObjectRepository.Create("Checkout", tenantId);
        repository.SetElement("Checkout", "submitButton", MakeLocator("button.submit-v1", description: "the submit button"));
        await repositories.Save(repository, CancellationToken.None);

        var stepParams = new Dictionary<string, object?> { ["selector"] = RepositoryRefParam("Checkout", "submitButton") };

        // Three independent flows, all referencing the same entry, authored once.
        for (var i = 0; i < 3; i++)
        {
            var before = await RunOneStepAndGetEchoedParams(engine, tenantId, stepParams);
            Assert.Equal("button.submit-v1", Assert.IsType<Locator>(before["selector"]).Primary.Value);
        }

        // One rename. No flow was touched above or below this line.
        repository.SetElement("Checkout", "submitButton", MakeLocator("button.submit-v2", description: "the submit button"));
        await repositories.Save(repository, CancellationToken.None);

        for (var i = 0; i < 3; i++)
        {
            var after = await RunOneStepAndGetEchoedParams(engine, tenantId, stepParams);
            Assert.Equal("button.submit-v2", Assert.IsType<Locator>(after["selector"]).Primary.Value);
        }
    }

    [Fact]
    public async Task A_missing_repository_entry_fails_the_step_naming_it()
    {
        var (engine, _) = BuildEngine();
        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("Missing entry flow", null, tenantId, UserId.New());
        flow.AddStep("Step", EchoModule.Kind, "echo", new Dictionary<string, object?>
        {
            ["selector"] = RepositoryRefParam("Checkout", "neverCreated"),
        });
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);

        var completed = await engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);

        var result = Assert.Single(completed.Results);
        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("could not be resolved", result.Error);
    }

    [Fact]
    public async Task Tenant_B_cannot_resolve_tenant_As_repository_entry()
    {
        var (engine, repositories) = BuildEngine();
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var repository = ObjectRepository.Create("Checkout", tenantA);
        repository.SetElement("Checkout", "submitButton", MakeLocator("button.submit", description: "the submit button"));
        await repositories.Save(repository, CancellationToken.None);

        var flow = Flow.CreateDraft("Cross-tenant flow", null, tenantB, UserId.New());
        flow.AddStep("Step", EchoModule.Kind, "echo", new Dictionary<string, object?>
        {
            ["selector"] = RepositoryRefParam("Checkout", "submitButton"),
        });
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantB);

        var completed = await engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);

        var result = Assert.Single(completed.Results);
        Assert.Equal(StepStatus.Failed, result.Status); // invisible, same as not existing
    }

    // ── Flow.Publish's own existence check (IRepositoryEntryExistsChecker) ──

    [Fact]
    public async Task Publish_fails_naming_the_step_when_the_referenced_repository_entry_is_missing()
    {
        var tenantId = TenantId.New();
        var repositories = new InMemoryObjectRepositoryRepository(new InMemoryUnitOfWorkFactory(new InMemoryDomainEventDispatcher()));
        var checker = new RepositoryEntryExistsChecker(new InMemoryObjectRepositoryElementQuery(repositories));

        var flow = Flow.CreateDraft("Publish validation flow", null, tenantId, UserId.New());
        flow.AddStep("Step", EchoModule.Kind, "echo", new Dictionary<string, object?>
        {
            ["selector"] = RepositoryRefParam("Checkout", "neverCreated"),
        });

        var resolver = new AlwaysResolvingModuleActionResolver();
        var result = await flow.Publish(resolver, null, checker, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Code == "unresolved_repository_entry");
    }

    [Fact]
    public async Task Publish_succeeds_when_the_referenced_repository_entry_exists()
    {
        var tenantId = TenantId.New();
        var uowFactory = new InMemoryUnitOfWorkFactory(new InMemoryDomainEventDispatcher());
        var repositories = new InMemoryObjectRepositoryRepository(uowFactory);
        var repository = ObjectRepository.Create("Checkout", tenantId);
        repository.SetElement("Checkout", "submitButton", MakeLocator("button.submit", description: "the submit button"));
        await repositories.Save(repository, CancellationToken.None);
        await uowFactory.Create().Commit(CancellationToken.None); // Save() only stages -- a real commit is needed outside FlowEngine's own per-step cycle
        var checker = new RepositoryEntryExistsChecker(new InMemoryObjectRepositoryElementQuery(repositories));

        var flow = Flow.CreateDraft("Publish validation flow", null, tenantId, UserId.New());
        flow.AddStep("Step", EchoModule.Kind, "echo", new Dictionary<string, object?>
        {
            ["selector"] = RepositoryRefParam("Checkout", "submitButton"),
        });

        var resolver = new AlwaysResolvingModuleActionResolver();
        var result = await flow.Publish(resolver, null, checker, CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Publish_without_a_checker_skips_the_repository_check_entirely()
    {
        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("Publish validation flow, no checker", null, tenantId, UserId.New());
        flow.AddStep("Step", EchoModule.Kind, "echo", new Dictionary<string, object?>
        {
            ["selector"] = RepositoryRefParam("Checkout", "neverCreated"),
        });

        var resolver = new AlwaysResolvingModuleActionResolver();
        var result = await flow.Publish(resolver, CancellationToken.None); // the original 2-arg overload

        Assert.True(result.Succeeded);
    }

    private sealed class AlwaysResolvingModuleActionResolver : IModuleActionResolver
    {
        public Task<Yukti.Domain.ModulePlugin.ActionSchema?> TryGetActionSchema(Yukti.Domain.ModulePlugin.ModuleKind module, string action, CancellationToken ct) =>
            Task.FromResult<Yukti.Domain.ModulePlugin.ActionSchema?>(new Yukti.Domain.ModulePlugin.ActionSchema
            {
                ActionName = action,
                Description = "test",
                Parameters = Array.Empty<Yukti.Domain.ModulePlugin.ParamSpec>(),
            });
    }
}
