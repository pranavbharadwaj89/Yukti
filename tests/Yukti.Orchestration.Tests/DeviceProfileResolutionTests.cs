using Microsoft.Extensions.Logging;
using Xunit;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.MobileTesting;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;
using Yukti.Infrastructure.InMemory.Modules;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// Mobile Testing Module Spec, MOB-T2: a flow's declared "mobile" config resolves a `profileName`
/// against the CURRENT DeviceProfile on every run (FlowEngine.ResolveDeviceProfile), the same
/// resolved-fresh-every-run shape RequestTemplateResolutionTests already proves for templateId.
/// No live Appium server is available in this environment (same constraint MOB-T1's own tests
/// document) -- these prove resolution and merge genuinely happened by asserting on
/// MobileModule.Setup()'s own connection-failure message, which only ever names the *resolved*
/// appiumUrl (MobileModule itself never sees a profile name or a repository).
/// </summary>
public sealed class DeviceProfileResolutionTests
{
    private static (FlowEngine Engine, InMemoryDeviceProfileRepository Profiles, InMemoryUnitOfWorkFactory UowFactory) BuildEngine()
    {
        var loggerFactory = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Warning));
        var dispatcher = new InMemoryDomainEventDispatcher();
        var uowFactory = new InMemoryUnitOfWorkFactory(dispatcher);
        var runRepo = new InMemoryFlowRunRepository(uowFactory);

        var registry = new ModuleRegistry();
        registry.Register(new MobileModule());
        var moduleRegistrations = new InMemoryModuleRegistrationRepository(uowFactory);
        var moduleDispatcher = new ModuleDispatcher(registry, moduleRegistrations, new ModuleExecutionStrategySelector(), loggerFactory.CreateLogger<ModuleDispatcher>());
        var variableStore = new VariableStore();
        var retryHandler = new RetryFlakeHandler(loggerFactory.CreateLogger<RetryFlakeHandler>());
        var profiles = new InMemoryDeviceProfileRepository(uowFactory);
        var engine = new FlowEngine(moduleDispatcher, registry, variableStore, retryHandler, runRepo, uowFactory,
            loggerFactory.CreateLogger<FlowEngine>(), deviceProfiles: profiles);
        return (engine, profiles, uowFactory);
    }

    /// <summary>InMemoryDeviceProfileRepository.Save only STAGES the write (same shape every
    /// in-memory repository here has) -- the real command handler's AuditableCommandHandler base
    /// commits it before the real API ever lets a flow run resolve it. FlowEngine.Execute's own
    /// first commit (run.Start()) happens AFTER device-profile resolution now (unlike templateId's
    /// per-step, post-run.Start() resolution), so seeding a profile for these tests needs its own
    /// explicit commit -- this is a test-seeding-order fix, not a production gap.</summary>
    private static async Task SeedProfile(InMemoryDeviceProfileRepository profiles, InMemoryUnitOfWorkFactory uowFactory, DeviceProfile profile)
    {
        await profiles.Save(profile, CancellationToken.None);
        await using var uow = uowFactory.Create();
        await uow.Commit(CancellationToken.None);
    }

    private static Flow FlowWithOneMobileStep(TenantId tenantId)
    {
        var flow = Flow.CreateDraft("Device profile resolution flow", null, tenantId, UserId.New());
        flow.AddStep("Tap", ModuleKind.Mobile, "tap", new Dictionary<string, object?> { ["selector"] = "/foo" });
        return flow;
    }

    [Fact]
    public async Task A_missing_profile_fails_the_run_naming_it_before_any_step_dispatches()
    {
        var (engine, _, _) = BuildEngine();
        var tenantId = TenantId.New();
        var flow = FlowWithOneMobileStep(tenantId);
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId, new Dictionary<string, object?>
        {
            ["mobile"] = new Dictionary<string, object?> { ["profileName"] = "pixel-7-android-14" },
        });

        var ex = await Assert.ThrowsAsync<Yukti.Orchestration.DeviceProfileResolutionException>(
            () => engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None));

        Assert.Contains("pixel-7-android-14", ex.Message);
    }

    [Fact]
    public async Task A_resolved_profile_supplies_its_appiumUrl_to_Setup_even_though_the_flow_only_named_it()
    {
        var (engine, profiles, uowFactory) = BuildEngine();
        var tenantId = TenantId.New();
        var profile = DeviceProfile.Create(
            "pixel-7-android-14", tenantId, MobilePlatform.Android, null, "pixel-7-android-14",
            null, "http://127.0.0.1:1", DeviceEndpointKind.Local, null); // nothing ever listens on port 1
        await SeedProfile(profiles, uowFactory, profile);

        var flow = FlowWithOneMobileStep(tenantId);
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId, new Dictionary<string, object?>
        {
            ["mobile"] = new Dictionary<string, object?> { ["profileName"] = "pixel-7-android-14" },
        });

        var ex = await Assert.ThrowsAsync<MobileSessionException>(
            () => engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None));

        Assert.Contains("No Appium server reachable", ex.Message);
        Assert.Contains("http://127.0.0.1:1", ex.Message);
    }

    [Fact]
    public async Task A_flows_own_literal_appiumUrl_overrides_the_resolved_profiles_appiumUrl()
    {
        var (engine, profiles, uowFactory) = BuildEngine();
        var tenantId = TenantId.New();
        var profile = DeviceProfile.Create(
            "pixel-7-android-14", tenantId, MobilePlatform.Android, null, "pixel-7-android-14",
            null, "http://127.0.0.1:1", DeviceEndpointKind.Local, null);
        await SeedProfile(profiles, uowFactory, profile);

        var flow = FlowWithOneMobileStep(tenantId);
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId, new Dictionary<string, object?>
        {
            ["mobile"] = new Dictionary<string, object?>
            {
                ["profileName"] = "pixel-7-android-14",
                ["appiumUrl"] = "http://127.0.0.1:2", // the flow's own override, a different unreachable port
            },
        });

        var ex = await Assert.ThrowsAsync<MobileSessionException>(
            () => engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None));

        Assert.Contains("http://127.0.0.1:2", ex.Message);
        Assert.DoesNotContain("http://127.0.0.1:1", ex.Message);
    }

    [Fact]
    public async Task No_repository_configured_leaves_literal_mobile_config_untouched_same_as_every_existing_construction_site()
    {
        // Mirrors RequestTemplateResolutionTests' own "every existing test/CLI construction site
        // keeps compiling and behaving identically" guarantee -- an engine built the original way
        // (no deviceProfiles: argument) must not change behavior for a flow with no profileName.
        var loggerFactory = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Warning));
        var dispatcher = new InMemoryDomainEventDispatcher();
        var uowFactory = new InMemoryUnitOfWorkFactory(dispatcher);
        var runRepo = new InMemoryFlowRunRepository(uowFactory);
        var registry = new ModuleRegistry();
        registry.Register(new MobileModule());
        var moduleRegistrations = new InMemoryModuleRegistrationRepository(uowFactory);
        var moduleDispatcher = new ModuleDispatcher(registry, moduleRegistrations, new ModuleExecutionStrategySelector(), loggerFactory.CreateLogger<ModuleDispatcher>());
        var engine = new FlowEngine(moduleDispatcher, registry, new VariableStore(), new RetryFlakeHandler(loggerFactory.CreateLogger<RetryFlakeHandler>()),
            runRepo, uowFactory, loggerFactory.CreateLogger<FlowEngine>()); // no deviceProfiles argument at all

        var tenantId = TenantId.New();
        var flow = FlowWithOneMobileStep(tenantId);
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId, new Dictionary<string, object?>
        {
            ["mobile"] = new Dictionary<string, object?>
            {
                ["platformName"] = "Android",
                ["deviceName"] = "pixel-7-android-14",
                ["appiumUrl"] = "http://127.0.0.1:1",
            },
        });

        var ex = await Assert.ThrowsAsync<MobileSessionException>(
            () => engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None));

        Assert.Contains("http://127.0.0.1:1", ex.Message);
    }
}
