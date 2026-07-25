using Yukti.Api;
using Yukti.Application.Abstractions;
using Yukti.Application.Execution;
using Yukti.Application.FlowAuthoring;
using Yukti.Application.ModulePlugin;
using Yukti.Contracts;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;
using Yukti.Infrastructure.InMemory.Modules;
using Yukti.Orchestration;

var builder = WebApplication.CreateBuilder(args);

// ---- Composition root ----
// Mirrors Yukti.Host's manual wiring — no DI container abstraction needed,
// only the built-in ASP.NET Core container. All infra here is the same
// demo-grade in-memory implementation Yukti.Host uses; swapping to a real
// EF Core + PostgreSQL Yukti.Infrastructure project (once DB access is
// available) means registering different implementations of the same
// IFlowRepository / IFlowRunRepository / IModuleRegistrationRepository /
// IUnitOfWorkFactory interfaces here — no other file in this project changes.
builder.Services.AddSingleton<InMemoryDomainEventDispatcher>();
builder.Services.AddSingleton<IDomainEventDispatcher>(sp => sp.GetRequiredService<InMemoryDomainEventDispatcher>());
builder.Services.AddSingleton<InMemoryUnitOfWorkFactory>();
builder.Services.AddSingleton<IUnitOfWorkFactory>(sp => sp.GetRequiredService<InMemoryUnitOfWorkFactory>());

builder.Services.AddSingleton<IFlowRepository, InMemoryFlowRepository>();
builder.Services.AddSingleton<IFlowRunRepository, InMemoryFlowRunRepository>();
builder.Services.AddSingleton<IModuleRegistrationRepository, InMemoryModuleRegistrationRepository>();
builder.Services.AddSingleton<IModuleActionResolver, ModuleActionResolver>();
builder.Services.AddSingleton<ICredentialResolver, InMemoryCredentialResolver>();

builder.Services.AddSingleton<ApiModule>();
builder.Services.AddSingleton<LogsModule>();
builder.Services.AddSingleton<IModuleRegistry>(sp =>
{
    var registry = new ModuleRegistry();
    registry.Register(sp.GetRequiredService<ApiModule>());
    registry.Register(sp.GetRequiredService<LogsModule>());
    return registry;
});
builder.Services.AddSingleton<IModuleDispatcher, ModuleDispatcher>();
builder.Services.AddSingleton<IVariableStore, VariableStore>();
builder.Services.AddSingleton<IRetryFlakeHandler, RetryFlakeHandler>();
builder.Services.AddSingleton<FlowEngine>();

builder.Services.AddScoped<CreateFlowCommandHandler>();
builder.Services.AddScoped<AddFlowStepCommandHandler>();
builder.Services.AddScoped<PublishFlowCommandHandler>();
builder.Services.AddScoped<TriggerFlowRunCommandHandler>();
builder.Services.AddScoped<CancelFlowRunCommandHandler>();
builder.Services.AddScoped<RegisterModuleCommandHandler>();

var app = builder.Build();

// ---- Seed built-in module registrations at startup ----
// Flow.Publish validates every step's (module, action) against a
// registered ModuleRegistration (Volume 1 Part II §9.2) — without this,
// every flow would fail to publish. Real deployments will do this via a
// proper module-marketplace install flow (Volume 0); for now, the two
// ported built-in modules register themselves the same way Yukti.Host's
// smoke test does.
var systemUserId = UserId.New();
using (var scope = app.Services.CreateScope())
{
    var registerHandler = scope.ServiceProvider.GetRequiredService<RegisterModuleCommandHandler>();
    var apiModule = scope.ServiceProvider.GetRequiredService<ApiModule>();
    var logsModule = scope.ServiceProvider.GetRequiredService<LogsModule>();

    await registerHandler.Handle(new RegisterModuleCommand(
        ModuleKind.Api, "API Automation", TrustTier.BuiltIn, apiModule.GetSupportedActions(), apiModule.ContractVersion, systemUserId), default);
    await registerHandler.Handle(new RegisterModuleCommand(
        ModuleKind.Logs, "Log Automation", TrustTier.BuiltIn, logsModule.GetSupportedActions(), logsModule.ContractVersion, systemUserId), default);
}

// ---- No auth layer yet (Volume 1 Part IV is not built) ----
// Every request runs as one fixed demo tenant/user until real
// authentication/multi-tenancy exists. This is the same kind of
// deliberate, documented shortcut as Yukti.Host's smoke test — not a
// silent gap. Replace with real principal resolution (JWT/session →
// TenantId/UserId) when Part IV lands.
var demoTenantId = TenantId.New();
var demoUserId = UserId.New();

const string flowNotFound = "Flow not found.";
const string runNotFound = "FlowRun not found.";

var flows = app.MapGroup("/api/flows").WithTags("Flows");

flows.MapPost("/", async (CreateFlowRequest req, CreateFlowCommandHandler handler, CancellationToken ct) =>
{
    var flowId = await handler.Handle(new CreateFlowCommand(req.Name, req.Description, demoTenantId, demoUserId), ct);
    return Results.Created($"/api/flows/{flowId.Value}", new { flowId = flowId.Value });
});

flows.MapGet("/{flowId:guid}", async (Guid flowId, IFlowRepository repo, CancellationToken ct) =>
{
    var flow = await repo.GetById(new FlowId(flowId), ct);
    return flow is null ? Results.NotFound(new { error = flowNotFound }) : Results.Ok(FlowResponse.From(flow));
});

flows.MapPost("/{flowId:guid}/steps", async (Guid flowId, AddStepRequest req, AddFlowStepCommandHandler handler, CancellationToken ct) =>
{
    try
    {
        await handler.Handle(new AddFlowStepCommand(
            new FlowId(flowId), req.Name, ModuleKind.Custom(req.Module), req.Action,
            JsonParamNormalizer.Normalize(req.Params), req.SaveAs, req.When), ct);
        return Results.NoContent();
    }
    catch (InvalidOperationException)
    {
        return Results.NotFound(new { error = flowNotFound });
    }
    catch (DomainException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

flows.MapPost("/{flowId:guid}/publish", async (Guid flowId, PublishFlowCommandHandler handler, CancellationToken ct) =>
{
    try
    {
        var result = await handler.Handle(new PublishFlowCommand(new FlowId(flowId), demoUserId), ct);
        return Results.Ok(PublishResponse.From(result));
    }
    catch (InvalidOperationException)
    {
        return Results.NotFound(new { error = flowNotFound });
    }
});

// Trigger + execute synchronously. There is no background job/queue
// infrastructure in this repo yet (that's Volume 1 Part V / Volume 4
// territory) — a real deployment would enqueue the run and let a worker
// call FlowEngine.Execute out of band, returning 202 Accepted immediately.
// This endpoint runs the flow inline and returns the completed result, a
// deliberate, temporary simplification analogous to Yukti.Host's smoke test.
flows.MapPost("/{flowId:guid}/runs", async (
    Guid flowId, TriggerRunRequest? req,
    IFlowRepository flowRepo, IFlowRunRepository runRepo,
    TriggerFlowRunCommandHandler triggerHandler, FlowEngine engine, ICredentialResolver credentials,
    CancellationToken ct) =>
{
    var flow = await flowRepo.GetById(new FlowId(flowId), ct);
    if (flow is null) return Results.NotFound(new { error = flowNotFound });
    if (flow.Status != FlowStatus.Published)
        return Results.BadRequest(new { error = $"Flow is {flow.Status}; only Published flows can be run." });

    var runId = await triggerHandler.Handle(
        new TriggerFlowRunCommand(flow.Id, RunTrigger.Api, JsonParamNormalizer.Normalize(req?.VariableOverrides), demoTenantId), ct);

    var run = (await runRepo.GetById(runId, ct))!;
    var completed = await engine.Execute(flow, run, credentials, ct);

    return Results.Ok(FlowRunResponse.From(completed));
});

var runs = app.MapGroup("/api/runs").WithTags("Runs");

runs.MapGet("/{runId:guid}", async (Guid runId, IFlowRunRepository repo, CancellationToken ct) =>
{
    var run = await repo.GetById(new FlowRunId(runId), ct);
    return run is null ? Results.NotFound(new { error = runNotFound }) : Results.Ok(FlowRunResponse.From(run));
});

runs.MapPost("/{runId:guid}/cancel", async (Guid runId, CancelFlowRunCommandHandler handler, CancellationToken ct) =>
{
    try
    {
        await handler.Handle(new CancelFlowRunCommand(new FlowRunId(runId), demoUserId), ct);
        return Results.NoContent();
    }
    catch (InvalidOperationException)
    {
        return Results.NotFound(new { error = runNotFound });
    }
});

// FR-ORCH-7: introspection surface a flow-authoring GUI renders from
// directly — zero hardcoded per-module knowledge on the client side.
app.MapGet("/api/modules", async (IModuleRegistry registry, IModuleRegistrationRepository registrations, CancellationToken ct) =>
{
    var responses = new List<ModuleResponse>();
    foreach (var module in registry.All)
    {
        var registration = await registrations.GetByKind(module.Kind, null, ct);
        responses.Add(new ModuleResponse(
            module.Kind.Value,
            registration?.DisplayName ?? module.Kind.Value,
            registration?.Trust.ToString() ?? "Unknown",
            module.ContractVersion,
            module.GetSupportedActions().Select(ActionSchemaResponse.From).ToList()));
    }
    return Results.Ok(responses);
}).WithTags("Modules");

app.MapGet("/", () => Results.Ok(new { service = "Yukti.Api", status = "running" }));

app.Run();
