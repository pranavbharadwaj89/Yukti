using Microsoft.Extensions.Logging;
using Yukti.Application.Abstractions;
using Yukti.Application.Execution;
using Yukti.Application.FlowAuthoring;
using Yukti.Application.IdentityAccess;
using Yukti.Application.ModulePlugin;
using Yukti.Contracts;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;
using Yukti.Infrastructure.InMemory.Modules;
using Yukti.Orchestration;

namespace Yukti.Cli;

/// <summary>
/// In-process composition root: in-memory repositories, a single default
/// tenant, every built-in module registered. The same wiring pattern
/// Yukti.Host's fixed smoke scenario already proved works — generalized
/// here into a reusable host instead of one hardcoded flow, which is the
/// entire difference between the two (D2-T6). Every handler below is the
/// identical Application-layer type the real API uses; nothing here is a
/// CLI-specific reimplementation of validation or execution.
/// </summary>
public sealed class CliHost
{
    public required TenantId TenantId { get; init; }
    public required UserId UserId { get; init; }
    public required FlowEngine Engine { get; init; }
    public required IFlowRepository Flows { get; init; }
    public required IFlowRunRepository Runs { get; init; }
    public required CreateFlowCommandHandler CreateFlow { get; init; }
    public required AddFlowStepCommandHandler AddStep { get; init; }
    public required PublishFlowCommandHandler Publish { get; init; }
    public required TriggerFlowRunCommandHandler TriggerRun { get; init; }
    public required ICredentialResolver Credentials { get; init; }
    public required IUnitOfWorkFactory UowFactory { get; init; }
    public required InMemoryDomainEventDispatcher Dispatcher { get; init; }

    public static async Task<CliHost> Build(ILoggerFactory loggerFactory, CancellationToken ct)
    {
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
        var auditRepository = new InMemoryAuditRepository();

        var tenantId = TenantId.New();
        var tenantAccessor = new FixedTenantContextAccessor(tenantId);
        var tenantGuard = new TenantGuard(tenantAccessor);

        var adminRole = Role.CreateBaselineAdministrator();
        await roleRepo.Save(adminRole, ct);
        var adminUser = User.Register("cli@yukti.local", "Yukti CLI", tenantId, hasher.Hash("unused"));
        adminUser.AssignRole(adminRole.Id);
        await userRepo.Save(adminUser, ct);
        await using (var seedUow = uowFactory.Create()) await seedUow.Commit(ct);
        var userId = adminUser.Id;

        var registry = new ModuleRegistry();
        registry.Register(new ApiModule());
        registry.Register(new WebModule());
        registry.Register(new LogsModule());
        registry.Register(new MobileModule());
        if (OperatingSystem.IsWindows())
            registry.Register(new DesktopUiModule());

        var registerHandler = new RegisterModuleCommandHandler(moduleRepo, permissions, uowFactory, auditRepository, tenantAccessor);
        foreach (var module in registry.All)
        {
            await registerHandler.Handle(new RegisterModuleCommand(
                module.Kind, module.Kind.Value, TrustTier.BuiltIn, module.GetSupportedActions(), module.ContractVersion, userId), ct);
        }

        var moduleDispatcher = new ModuleDispatcher(registry, moduleRepo, new ModuleExecutionStrategySelector(), loggerFactory.CreateLogger<ModuleDispatcher>());
        var variableStore = new VariableStore();
        var retryHandler = new RetryFlakeHandler(loggerFactory.CreateLogger<RetryFlakeHandler>());
        var engine = new FlowEngine(moduleDispatcher, registry, variableStore, retryHandler, runRepo, uowFactory, loggerFactory.CreateLogger<FlowEngine>());

        return new CliHost
        {
            TenantId = tenantId,
            UserId = userId,
            Engine = engine,
            Flows = flowRepo,
            Runs = runRepo,
            CreateFlow = new CreateFlowCommandHandler(flowRepo, permissions, uowFactory, auditRepository, tenantAccessor),
            AddStep = new AddFlowStepCommandHandler(flowRepo, permissions, tenantGuard, uowFactory, auditRepository, tenantAccessor),
            Publish = new PublishFlowCommandHandler(flowRepo, resolver, permissions, tenantGuard, uowFactory, auditRepository, tenantAccessor),
            TriggerRun = new TriggerFlowRunCommandHandler(runRepo, permissions, uowFactory, auditRepository, tenantAccessor),
            Credentials = new EnvironmentCredentialResolver(),
            UowFactory = uowFactory,
            Dispatcher = dispatcher,
        };
    }

    /// <summary>Host has no HTTP request/JWT — same stand-in Yukti.Host uses for its one fixed tenant.</summary>
    private sealed class FixedTenantContextAccessor : ITenantContextAccessor
    {
        public FixedTenantContextAccessor(TenantId tenantId) => CurrentTenantId = tenantId;
        public TenantId? CurrentTenantId { get; }
    }
}
