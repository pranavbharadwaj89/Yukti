using Microsoft.Extensions.Logging;
using Xunit;
using Yukti.Application.ApiTesting;
using Yukti.Contracts;
using Yukti.Domain.ApiTesting;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// API Testing Module Spec, API-T9: a step's `templateId` resolves against the CURRENT
/// RequestTemplate on every run (FlowEngine.ResolveRequestTemplate), and Flow.Publish rejects a
/// step referencing a template that doesn't exist when a real checker is supplied. Uses
/// VariableStoreTests' EchoModule (same project, public) to inspect exactly what params a step
/// received after template merge + interpolation.
/// </summary>
public sealed class RequestTemplateResolutionTests
{
    private static (FlowEngine Engine, InMemoryRequestTemplateRepository Templates) BuildEngine()
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
        var templates = new InMemoryRequestTemplateRepository(uowFactory);
        var engine = new FlowEngine(moduleDispatcher, registry, variableStore, retryHandler, runRepo, uowFactory,
            loggerFactory.CreateLogger<FlowEngine>(), requestTemplates: templates);
        return (engine, templates);
    }

    private static async Task<RequestTemplate> SeedTemplate(
        InMemoryRequestTemplateRepository templates, TenantId tenantId, string? baseUrl = "https://api.example.com",
        IReadOnlyDictionary<string, object?>? headers = null, object? auth = null, int? defaultTimeoutMs = null)
    {
        var template = RequestTemplate.Create("Orders API", tenantId, baseUrl,
            headers ?? new Dictionary<string, object?> { ["X-Common"] = "common-value", ["X-Override-Me"] = "template-value" },
            auth, defaultTimeoutMs);
        await templates.Save(template, CancellationToken.None);
        return template;
    }

    private static async Task<IReadOnlyDictionary<string, object?>> RunOneStepAndGetEchoedParams(
        FlowEngine engine, TenantId tenantId, Dictionary<string, object?> stepParams)
    {
        var flow = Flow.CreateDraft("Template resolution flow", null, tenantId, UserId.New());
        flow.AddStep("Step", EchoModule.Kind, "echo", stepParams);
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);
        var completed = await engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);
        var result = Assert.Single(completed.Results);
        Assert.Equal(StepStatus.Passed, result.Status);
        return Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(result.Data);
    }

    [Fact]
    public async Task A_step_referencing_a_template_inherits_its_baseUrl_headers_auth_and_timeout()
    {
        var (engine, templates) = BuildEngine();
        var tenantId = TenantId.New();
        var template = await SeedTemplate(templates, tenantId,
            auth: new Dictionary<string, object?> { ["type"] = "bearer", ["token"] = "tpl-token" },
            defaultTimeoutMs: 5000);

        var echoed = await RunOneStepAndGetEchoedParams(engine, tenantId, new Dictionary<string, object?>
        {
            ["templateId"] = template.Id.Value.ToString(),
        });

        Assert.Equal("https://api.example.com", echoed["url"]);
        var headers = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(echoed["headers"]);
        Assert.Equal("common-value", headers["X-Common"]);
        var auth = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(echoed["auth"]);
        Assert.Equal("tpl-token", auth["token"]);
        Assert.Equal(5000, echoed["timeoutMs"]);
    }

    [Fact]
    public async Task A_step_overriding_one_header_keeps_the_templates_other_headers()
    {
        var (engine, templates) = BuildEngine();
        var tenantId = TenantId.New();
        var template = await SeedTemplate(templates, tenantId);

        var echoed = await RunOneStepAndGetEchoedParams(engine, tenantId, new Dictionary<string, object?>
        {
            ["templateId"] = template.Id.Value.ToString(),
            ["headers"] = new Dictionary<string, object?> { ["X-Override-Me"] = "step-value" },
        });

        var headers = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(echoed["headers"]);
        Assert.Equal("common-value", headers["X-Common"]);
        Assert.Equal("step-value", headers["X-Override-Me"]);
    }

    [Fact]
    public async Task A_steps_own_absolute_url_is_used_as_is_ignoring_the_templates_baseUrl()
    {
        var (engine, templates) = BuildEngine();
        var tenantId = TenantId.New();
        var template = await SeedTemplate(templates, tenantId, baseUrl: "https://api.example.com");

        var echoed = await RunOneStepAndGetEchoedParams(engine, tenantId, new Dictionary<string, object?>
        {
            ["templateId"] = template.Id.Value.ToString(),
            ["url"] = "https://override.example.com/orders",
        });

        Assert.Equal("https://override.example.com/orders", echoed["url"]);
    }

    [Fact]
    public async Task A_steps_own_relative_url_is_joined_onto_the_templates_baseUrl()
    {
        var (engine, templates) = BuildEngine();
        var tenantId = TenantId.New();
        var template = await SeedTemplate(templates, tenantId, baseUrl: "https://api.example.com/");

        var echoed = await RunOneStepAndGetEchoedParams(engine, tenantId, new Dictionary<string, object?>
        {
            ["templateId"] = template.Id.Value.ToString(),
            ["url"] = "/orders/123",
        });

        Assert.Equal("https://api.example.com/orders/123", echoed["url"]);
    }

    [Fact]
    public async Task A_steps_own_auth_fully_replaces_the_templates_auth()
    {
        var (engine, templates) = BuildEngine();
        var tenantId = TenantId.New();
        var template = await SeedTemplate(templates, tenantId,
            auth: new Dictionary<string, object?> { ["type"] = "bearer", ["token"] = "tpl-token" });

        var echoed = await RunOneStepAndGetEchoedParams(engine, tenantId, new Dictionary<string, object?>
        {
            ["templateId"] = template.Id.Value.ToString(),
            ["auth"] = new Dictionary<string, object?> { ["type"] = "basic", ["username"] = "u", ["password"] = "p" },
        });

        var auth = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(echoed["auth"]);
        Assert.Equal("basic", auth["type"]);
    }

    [Fact]
    public async Task Changing_the_template_updates_the_next_run_with_no_flow_edits()
    {
        var (engine, templates) = BuildEngine();
        var tenantId = TenantId.New();
        var template = await SeedTemplate(templates, tenantId, baseUrl: "https://v1.example.com");

        var stepParams = new Dictionary<string, object?> { ["templateId"] = template.Id.Value.ToString() };
        var firstRun = await RunOneStepAndGetEchoedParams(engine, tenantId, stepParams);
        Assert.Equal("https://v1.example.com", firstRun["url"]);

        template.Update("Orders API", "https://v2.example.com", null, null, null);
        await templates.Save(template, CancellationToken.None);

        // Same step params, no flow edit at all -- only the template changed.
        var secondRun = await RunOneStepAndGetEchoedParams(engine, tenantId, stepParams);
        Assert.Equal("https://v2.example.com", secondRun["url"]);
    }

    [Fact]
    public async Task A_missing_template_fails_the_step_naming_it()
    {
        var (engine, _) = BuildEngine();
        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("Missing template flow", null, tenantId, UserId.New());
        flow.AddStep("Step", EchoModule.Kind, "echo", new Dictionary<string, object?> { ["templateId"] = Guid.NewGuid().ToString() });
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);

        var completed = await engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);

        var result = Assert.Single(completed.Results);
        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("could not be resolved", result.Error);
    }

    [Fact]
    public async Task Publish_fails_naming_the_step_when_the_referenced_template_is_missing()
    {
        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("Publish validation flow", null, tenantId, UserId.New());
        flow.AddStep("Step", EchoModule.Kind, "echo", new Dictionary<string, object?> { ["templateId"] = Guid.NewGuid().ToString() });

        var resolver = new AlwaysResolvingModuleActionResolver();
        var checker = new AlwaysMissingTemplateChecker();
        var result = await flow.Publish(resolver, checker, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Code == "unresolved_request_template");
    }

    [Fact]
    public async Task Publish_without_a_checker_skips_the_template_check_entirely()
    {
        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("Publish validation flow, no checker", null, tenantId, UserId.New());
        flow.AddStep("Step", EchoModule.Kind, "echo", new Dictionary<string, object?> { ["templateId"] = Guid.NewGuid().ToString() });

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

    private sealed class AlwaysMissingTemplateChecker : IRequestTemplateExistsChecker
    {
        public Task<bool> ExistsAsync(string requestTemplateId, TenantId tenantId, CancellationToken ct) => Task.FromResult(false);
    }
}
