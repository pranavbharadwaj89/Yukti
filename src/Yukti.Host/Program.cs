using Yukti.Application.Abstractions;
using Yukti.Application.Execution;
using Yukti.Application.FlowAuthoring;
using Yukti.Application.IdentityAccess;
using Yukti.Application.ModulePlugin;
using Yukti.Contracts;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;
using Yukti.Infrastructure.InMemory.Modules;
using Yukti.Orchestration;

Console.WriteLine("=== Yukti Backend Core — end-to-end smoke run ===\n");

// ---- Composition root (manual — no DI container package needed) ----
var dispatcher = new InMemoryDomainEventDispatcher();
var uowFactory = new InMemoryUnitOfWorkFactory(dispatcher);

var flowRepo = new InMemoryFlowRepository(uowFactory);
var runRepo = new InMemoryFlowRunRepository(uowFactory);
var moduleRepo = new InMemoryModuleRegistrationRepository(uowFactory);
var resolver = new ModuleActionResolver(moduleRepo);

var userRepo = new InMemoryUserRepository(uowFactory);
var roleRepo = new InMemoryRoleRepository(uowFactory);
var permissions = new PermissionChecker(userRepo, roleRepo);
var hasher = new Pbkdf2PasswordHasher();

var moduleRegistry = new ModuleRegistry();
var apiModule = new ApiModule();
var logsModule = new LogsModule();
moduleRegistry.Register(apiModule);
moduleRegistry.Register(logsModule);

var moduleDispatcher = new ModuleDispatcher(moduleRegistry);
var variableStore = new VariableStore();
var retryHandler = new RetryFlakeHandler();
var flowEngine = new FlowEngine(moduleDispatcher, variableStore, retryHandler, runRepo, uowFactory);
var credentials = new InMemoryCredentialResolver();

var tenantId = TenantId.New();

// ---- Bootstrap an Administrator so EnsurePermission (FR-AUTHZ-02) doesn't
// block this smoke test's every command — same seeding pattern Yukti.Api
// uses at startup, done manually here since Host has no DI container. ----
var adminRole = Role.CreateBaselineAdministrator();
await roleRepo.Save(adminRole, default);
var adminUser = Yukti.Domain.IdentityAccess.User.Register("smoke-test@yukti.local", "Smoke Test Runner", tenantId, hasher.Hash("unused"));
adminUser.AssignRole(adminRole.Id);
await userRepo.Save(adminUser, default);
await using (var seedUow = uowFactory.Create()) await seedUow.Commit(default);
var userId = adminUser.Id;

// ---- Live progress subscription — proves domain events actually fire ----
dispatcher.Subscribe<Yukti.Domain.Events.StepCompletedEvent>(evt =>
{
    var icon = evt.Result.Status switch { StepStatus.Passed => "✅", StepStatus.Failed => "❌", _ => "⏭️ " };
    Console.WriteLine($"  {icon} [{evt.Result.Module}] {evt.Result.StepName} ({evt.Result.Duration.TotalMilliseconds:F0}ms)");
    if (evt.Result.Error is not null) Console.WriteLine($"     ↳ {evt.Result.Error}");
    if (evt.Result.Message is not null) Console.WriteLine($"     ↳ {evt.Result.Message}");
});

// ---- Register modules (Application layer, backs Flow.Publish's validation) ----
var registerHandler = new RegisterModuleCommandHandler(moduleRepo, permissions, uowFactory);
await registerHandler.Handle(new RegisterModuleCommand(
    ModuleKind.Api, "API Automation", TrustTier.BuiltIn, apiModule.GetSupportedActions(), apiModule.ContractVersion, userId), default);
await registerHandler.Handle(new RegisterModuleCommand(
    ModuleKind.Logs, "Log Automation", TrustTier.BuiltIn, logsModule.GetSupportedActions(), logsModule.ContractVersion, userId), default);

Console.WriteLine("Registered modules: api, logs\n");

// ---- Author a flow: GitHub API chaining + log rule/anomaly checks ----
var createHandler = new CreateFlowCommandHandler(flowRepo, permissions, uowFactory);
var addStepHandler = new AddFlowStepCommandHandler(flowRepo, permissions, uowFactory);
var publishHandler = new PublishFlowCommandHandler(flowRepo, resolver, permissions, uowFactory);

var flowId = await createHandler.Handle(
    new CreateFlowCommand("Yukti smoke test", "API chaining + log checks", tenantId, userId), default);

const string sampleLog = """
[2026-07-25T09:00] INFO  boot sequence complete
[2026-07-25T09:01] INFO  request GET /health 200
[2026-07-25T09:02] WARN  slow query 812ms
[2026-07-25T09:03] ERROR db connection reset, retrying
[2026-07-25T09:07] ERROR payment gateway timeout for order 4471
[2026-07-25T09:07] ERROR payment gateway timeout for order 4472
[2026-07-25T09:07] ERROR payment gateway timeout for order 4473
[2026-07-25T09:07] ERROR payment gateway timeout for order 4474
[2026-07-25T09:07] ERROR payment gateway timeout for order 4475
[2026-07-25T09:07] ERROR payment gateway timeout for order 4476
[2026-07-25T09:08] INFO  request GET /health 200
""";

await addStepHandler.Handle(new AddFlowStepCommand(
    flowId, "Fetch the Anthropic GitHub org", ModuleKind.Api, "request",
    new Dictionary<string, object?> { ["url"] = "https://api.github.com/orgs/anthropics", ["method"] = "GET" },
    SaveAs: "orgInfo", When: null, RequestedBy: userId), default);

await addStepHandler.Handle(new AddFlowStepCommand(
    flowId, "Fetch the org's repos via chained URL", ModuleKind.Api, "request",
    new Dictionary<string, object?> { ["url"] = "{{vars.orgInfo.repos_url}}", ["method"] = "GET" },
    SaveAs: null, When: null, RequestedBy: userId), default);

await addStepHandler.Handle(new AddFlowStepCommand(
    flowId, "Enforce error budget", ModuleKind.Logs, "checkRules",
    new Dictionary<string, object?>
    {
        ["logText"] = sampleLog,
        ["rules"] = new List<object?>
        {
            new Dictionary<string, object?> { ["name"] = "bounded-errors", ["pattern"] = "ERROR", ["maxAllowed"] = 5 }
        }
    },
    SaveAs: null, When: null, RequestedBy: userId), default);

await addStepHandler.Handle(new AddFlowStepCommand(
    flowId, "Detect error-rate spikes", ModuleKind.Logs, "detectAnomalies",
    new Dictionary<string, object?> { ["logText"] = sampleLog, ["stdDevThreshold"] = 2.0 },
    SaveAs: null, When: null, RequestedBy: userId), default);

var flow = await flowRepo.GetById(flowId, default);
flow!.SetContinueOnFailure(true); // demo runs every step regardless, so we see the whole picture
await flowRepo.Save(flow, default);
await using (var uow = uowFactory.Create()) await uow.Commit(default);

var publishResult = await publishHandler.Handle(new PublishFlowCommand(flowId, userId), default);
Console.WriteLine($"Publish result: {(publishResult.Succeeded ? "SUCCEEDED" : "FAILED")}");
if (!publishResult.Succeeded)
{
    foreach (var error in publishResult.Errors) Console.WriteLine($"  - {error}");
    return;
}

// ---- Trigger and execute the run ----
var triggerHandler = new TriggerFlowRunCommandHandler(runRepo, permissions, uowFactory);
var runId = await triggerHandler.Handle(new TriggerFlowRunCommand(flowId, RunTrigger.Api, null, tenantId, userId), default);

Console.WriteLine($"\n▶ Executing FlowRun {runId}\n");

var publishedFlow = (await flowRepo.GetById(flowId, default))!;
var run = (await runRepo.GetById(runId, default))!;
var completedRun = await flowEngine.Execute(publishedFlow, run, credentials, default);

Console.WriteLine($"\n=== Result: {completedRun.Status} ===");
Console.WriteLine($"  Passed:  {completedRun.Results.Count(r => r.Status == StepStatus.Passed)}");
Console.WriteLine($"  Failed:  {completedRun.Results.Count(r => r.Status == StepStatus.Failed)}");
Console.WriteLine($"  Skipped: {completedRun.Results.Count(r => r.Status == StepStatus.Skipped)}");
Console.WriteLine($"  Flaky:   {completedRun.Results.Count(r => r.IsFlaky)}");
Console.WriteLine($"\nDuration: {(completedRun.FinishedAt - completedRun.StartedAt)!.Value.TotalMilliseconds:F0}ms");
