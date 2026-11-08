using Xunit;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// Mobile Testing Module Spec, MOB-T2: "Publish validation: a flow referencing a missing profile
/// fails with the existing 422-naming-the-step behaviour." Flow-scoped, not step-scoped (see
/// Flow.Publish's own doc comment on this check) -- MobileModule.Setup() opens its session once
/// per flow run from the flow's declared "mobile" variable, not from any one step's params, so
/// this checks DeclaredVariables rather than scanning steps[i].params the way the templateId/
/// RepositoryRef checks above it do. Same FakeModuleActionResolver double FlowPublishValidationTests
/// already uses.
/// </summary>
public sealed class DeviceProfilePublishValidationTests
{
    private static Flow NewDraftFlowWithOneMobileStep(TenantId tenantId)
    {
        var flow = Flow.CreateDraft("Device profile publish validation flow", null, tenantId, UserId.New());
        flow.AddStep("Tap", ModuleKind.Mobile, "tap", new Dictionary<string, object?> { ["selector"] = "/foo" });
        return flow;
    }

    private static FakeModuleActionResolver ResolverForTap() =>
        new FakeModuleActionResolver().Register(ModuleKind.Mobile, new ActionSchema
        {
            ActionName = "tap",
            Description = "test",
            Parameters = new[] { new ParamSpec { Name = "selector", Type = ParamType.String, Required = true, Description = "d" } },
        });

    private sealed class AlwaysMissingDeviceProfileChecker : IDeviceProfileExistsChecker
    {
        public Task<bool> ExistsAsync(string profileName, TenantId tenantId, CancellationToken ct) => Task.FromResult(false);
    }

    private sealed class AlwaysPresentDeviceProfileChecker : IDeviceProfileExistsChecker
    {
        public Task<bool> ExistsAsync(string profileName, TenantId tenantId, CancellationToken ct) => Task.FromResult(true);
    }

    [Fact]
    public async Task Publish_fails_when_the_declared_mobile_profile_is_missing()
    {
        var tenantId = TenantId.New();
        var flow = NewDraftFlowWithOneMobileStep(tenantId);
        flow.DeclareVariable("mobile", new Dictionary<string, object?> { ["profileName"] = "pixel-7-android-14" });

        var result = await flow.Publish(ResolverForTap(), null, null, new AlwaysMissingDeviceProfileChecker(), CancellationToken.None);

        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Errors);
        Assert.Equal("unresolved_device_profile", error.Code);
        Assert.Equal("variables.mobile.profileName", error.Field);
        Assert.Contains("pixel-7-android-14", error.Message);
    }

    [Fact]
    public async Task Publish_succeeds_when_the_declared_mobile_profile_exists()
    {
        var tenantId = TenantId.New();
        var flow = NewDraftFlowWithOneMobileStep(tenantId);
        flow.DeclareVariable("mobile", new Dictionary<string, object?> { ["profileName"] = "pixel-7-android-14" });

        var result = await flow.Publish(ResolverForTap(), null, null, new AlwaysPresentDeviceProfileChecker(), CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Publish_without_a_device_profile_checker_skips_the_check_entirely()
    {
        var tenantId = TenantId.New();
        var flow = NewDraftFlowWithOneMobileStep(tenantId);
        flow.DeclareVariable("mobile", new Dictionary<string, object?> { ["profileName"] = "pixel-7-android-14" });

        var result = await flow.Publish(ResolverForTap(), CancellationToken.None); // the original 2-arg overload

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task A_flow_with_no_declared_mobile_variable_is_unaffected_by_the_checker()
    {
        var tenantId = TenantId.New();
        var flow = NewDraftFlowWithOneMobileStep(tenantId); // no DeclareVariable("mobile", ...) at all

        var result = await flow.Publish(ResolverForTap(), null, null, new AlwaysMissingDeviceProfileChecker(), CancellationToken.None);

        Assert.True(result.Succeeded);
    }
}
