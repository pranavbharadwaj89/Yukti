using Xunit;
using Yukti.Contracts;
using Yukti.Domain.MobileTesting;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;
using Yukti.Infrastructure.InMemory.Modules;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// Mobile Testing Module Spec, MOB-T8 (P1) — mobile web and hybrid. No `✔` filter is given in the
/// spec for this task (it names one for every P0 task but not this P1 one); this file's own name
/// is the filter chosen here, recorded in plan/PLAN.md rather than left implicit.
///
/// Two checklist items, both already real before this task touched any production code --
/// proven here, not re-invented:
/// 1. "Automating a website in a mobile browser (Safari on iOS, Chrome on Android) via Appium":
///    Appium's own recipe for this is a `browserName` capability instead of `app` -- and
///    `MobileModule.BuildProfile`'s config-bag parsing (MOB-T1) already passes any key it doesn't
///    specifically reserve straight into `ExtraCapabilities`, which `Setup()` already feeds to
///    `AppiumOptions.AddAdditionalAppiumOption` for every entry. A flow author setting
///    `mobile.browserName` today already reaches the real Appium capabilities payload with zero
///    new code -- proven below.
/// 2. "A webview inside a native app, with context switching": MOB-T6's `switchContext` action
///    already implements this generically (Native/Webview, cross-platform,
///    `AppiumDeviceCommandExecutor.IsSupported` already covers it) -- nothing mobile-web-specific
///    to add on top.
///
/// No live Appium server is available in this environment (the same constraint every prior
/// Mobile task documents), so the actual browser launch and the actual webview context switch
/// aren't exercised end-to-end here -- what's proven is the config-bag plumbing that makes both
/// possible, which needs no driver.
/// </summary>
public sealed class MobileWebAndHybridTests
{
    private static ExecutionContext BuildContext() => new()
    {
        RunId = FlowRunId.New(),
        Variables = new Dictionary<string, object?>(),
        Credentials = new InMemoryCredentialResolver(),
        RunCancellation = CancellationToken.None,
    };

    [Theory]
    [InlineData("Android", "Chrome")]
    [InlineData("iOS", "Safari")]
    public async Task BuildProfile_already_passes_browserName_through_to_ExtraCapabilities_with_no_new_code(string platformName, string browserName)
    {
        var profile = await MobileModule.BuildProfile(new Dictionary<string, object?>
        {
            ["platformName"] = platformName,
            ["deviceName"] = "any-device",
            ["browserName"] = browserName,
        }, TenantId.New(), BuildContext(), CancellationToken.None);

        Assert.Equal(browserName, profile.ExtraCapabilities["browserName"]);
    }

    [Fact]
    public async Task A_mobile_web_profile_needs_no_app_capability_at_all()
    {
        // Appium's own "mobile web" recipe is browserName INSTEAD of app, not alongside it --
        // proves the config-bag path tolerates app being entirely absent.
        var profile = await MobileModule.BuildProfile(new Dictionary<string, object?>
        {
            ["platformName"] = "Android",
            ["deviceName"] = "any-device",
            ["browserName"] = "Chrome",
        }, TenantId.New(), BuildContext(), CancellationToken.None);

        Assert.Null(profile.App);
        Assert.Equal("Chrome", profile.ExtraCapabilities["browserName"]);
    }

    [Fact]
    public void SwitchContext_to_Webview_the_hybrid_use_cases_own_mechanism_is_already_supported_on_both_platforms()
    {
        // Re-asserts MOB-T6's own coverage specifically through MOB-T8's lens (the webview-
        // inside-a-native-app checklist item), not a duplicate of MobileDeviceCommandTests --
        // this is the one fact MOB-T8's second bullet actually depends on.
        var (androidSupported, _) = AppiumDeviceCommandExecutor.IsSupported(new SwitchContext(MobileContext.Webview), MobilePlatform.Android);
        var (iosSupported, _) = AppiumDeviceCommandExecutor.IsSupported(new SwitchContext(MobileContext.Webview), MobilePlatform.Ios);

        Assert.True(androidSupported);
        Assert.True(iosSupported);
    }
}
