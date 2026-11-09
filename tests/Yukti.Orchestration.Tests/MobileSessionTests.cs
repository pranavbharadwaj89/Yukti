using Xunit;
using Yukti.Contracts;
using Yukti.Domain.MobileTesting;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;
using Yukti.Infrastructure.InMemory.Modules;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// Mobile Testing Module Spec, MOB-T3: session lifecycle and app state. No live Appium server is
/// available in this environment (same constraint MOB-T1's own tests document), so Setup() can
/// never reach the point of actually holding an open session here either -- these prove exactly
/// what's provable without one: the Reset-strategy-to-Appium-capability mapping as a pure
/// function (<see cref="MobileModule.ResetCapabilitiesFor"/>), and that "reset"/
/// "sessionTimeoutSeconds" -- the two config keys FlowEngine.ResolveDeviceProfile (MOB-T2) uses to
/// carry a resolved profile's own settings into Setup()'s literal config bag -- are parsed
/// correctly by the same <see cref="MobileModule.BuildProfile"/> MOB-T1 already made public for
/// this exact reason.
///
/// Not provable here, flagged rather than invented: the actual behavioural isolation a real
/// Appium server/device would provide ("flow A writes state, flow B with Fast reset sees a clean
/// app") and the force-release-on-timeout path inside Run() (both need a session that Setup()
/// actually completed, which needs a live server). See plan/OPEN-QUESTIONS.md.
/// </summary>
public sealed class MobileSessionTests
{
    private static ExecutionContext BuildContext(IReadOnlyDictionary<string, object?> variables) => new()
    {
        RunId = FlowRunId.New(),
        Variables = variables,
        Credentials = new InMemoryCredentialResolver(),
        RunCancellation = CancellationToken.None,
    };

    [Theory]
    [InlineData(ResetStrategy.Full, false, true)]
    [InlineData(ResetStrategy.Fast, false, false)]
    [InlineData(ResetStrategy.None, true, false)]
    public void ResetCapabilitiesFor_maps_each_strategy_to_the_right_Appium_capabilities(ResetStrategy reset, bool expectedNoReset, bool expectedFullReset)
    {
        var (noReset, fullReset) = MobileModule.ResetCapabilitiesFor(reset);

        Assert.Equal(expectedNoReset, noReset);
        Assert.Equal(expectedFullReset, fullReset);
    }

    [Theory]
    [InlineData("Full", ResetStrategy.Full)]
    [InlineData("fast", ResetStrategy.Fast)] // case-insensitive, matches every other enum-from-string parse in this module
    [InlineData("None", ResetStrategy.None)]
    public async Task BuildProfile_parses_a_literal_reset_key(string literalReset, ResetStrategy expected)
    {
        var profile = await MobileModule.BuildProfile(new Dictionary<string, object?>
        {
            ["platformName"] = "Android",
            ["deviceName"] = "pixel-7-android-14",
            ["reset"] = literalReset,
        }, TenantId.New(), BuildContext(new Dictionary<string, object?>()), CancellationToken.None);

        Assert.Equal(expected, profile.Reset);
    }

    [Fact]
    public async Task BuildProfile_defaults_to_Fast_reset_when_no_literal_reset_key_is_present()
    {
        var profile = await MobileModule.BuildProfile(new Dictionary<string, object?>
        {
            ["platformName"] = "Android",
            ["deviceName"] = "pixel-7-android-14",
        }, TenantId.New(), BuildContext(new Dictionary<string, object?>()), CancellationToken.None);

        Assert.Equal(ResetStrategy.Fast, profile.Reset);
    }

    [Fact]
    public async Task BuildProfile_parses_a_literal_sessionTimeoutSeconds_key()
    {
        var profile = await MobileModule.BuildProfile(new Dictionary<string, object?>
        {
            ["platformName"] = "Android",
            ["deviceName"] = "pixel-7-android-14",
            ["sessionTimeoutSeconds"] = 42,
        }, TenantId.New(), BuildContext(new Dictionary<string, object?>()), CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(42), profile.SessionTimeout);
    }

    [Fact]
    public async Task BuildProfile_defaults_to_a_five_minute_session_timeout_when_absent()
    {
        var profile = await MobileModule.BuildProfile(new Dictionary<string, object?>
        {
            ["platformName"] = "Android",
            ["deviceName"] = "pixel-7-android-14",
        }, TenantId.New(), BuildContext(new Dictionary<string, object?>()), CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(5), profile.SessionTimeout);
    }

    [Fact]
    public async Task BuildProfile_treats_reset_and_sessionTimeoutSeconds_as_reserved_never_leaking_into_ExtraCapabilities()
    {
        var profile = await MobileModule.BuildProfile(new Dictionary<string, object?>
        {
            ["platformName"] = "Android",
            ["deviceName"] = "pixel-7-android-14",
            ["reset"] = "Full",
            ["sessionTimeoutSeconds"] = 42,
        }, TenantId.New(), BuildContext(new Dictionary<string, object?>()), CancellationToken.None);

        Assert.DoesNotContain("reset", profile.ExtraCapabilities.Keys);
        Assert.DoesNotContain("sessionTimeoutSeconds", profile.ExtraCapabilities.Keys);
    }
}
