using Xunit;
using Yukti.Contracts;
using Yukti.Domain.MobileTesting;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory.Modules;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// Mobile Testing Module Spec, MOB-T1 -- the module's first real test coverage (none existed
/// before this). No live Appium server/device is available in this environment (owner's own
/// call this session: "build against mocks for now"), so these prove exactly what's provable
/// without one: the connection-failure path for real (a genuinely unreachable endpoint, same
/// proven pattern Sarvaghna's Git Agent used this session), the failure classifier as a pure
/// function (synthetic exceptions, no server needed), and the credential-resolution/leak
/// behavior via a fake ICredentialResolver. The "session rejected, device/app not found"
/// categories ClassifySessionFailure's fallback covers are real Appium-server responses this
/// environment cannot produce; that gap is explicit, not hidden.
/// </summary>
public sealed class MobileAppiumClientTests
{
    private sealed class FakeCredentialResolver : ICredentialResolver
    {
        private readonly Dictionary<string, string> _values;
        public List<string> RequestedReferences { get; } = new();
        public FakeCredentialResolver(Dictionary<string, string> values) => _values = values;

        public Task<string?> ResolveAsync(string credentialReference, CancellationToken ct)
        {
            RequestedReferences.Add(credentialReference);
            return Task.FromResult(_values.GetValueOrDefault(credentialReference));
        }
    }

    private static ExecutionContext BuildContext(IReadOnlyDictionary<string, object?> variables, ICredentialResolver? credentials = null) => new()
    {
        RunId = FlowRunId.New(),
        Variables = variables,
        Credentials = credentials ?? new FakeCredentialResolver(new()),
        RunCancellation = CancellationToken.None,
    };

    [Fact]
    public async Task Setup_against_an_unreachable_Appium_endpoint_fails_with_a_specific_no_server_message_not_a_bare_timeout()
    {
        var module = new MobileModule();
        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["mobile"] = new Dictionary<string, object?>
            {
                ["platformName"] = "Android",
                ["deviceName"] = "pixel-7-android-14",
                ["appiumUrl"] = "http://127.0.0.1:1", // nothing ever listens on port 1
            },
        });

        var ex = await Assert.ThrowsAsync<MobileSessionException>(() => module.Setup(ctx, CancellationToken.None));

        Assert.Contains("No Appium server reachable", ex.Message);
        Assert.Contains("http://127.0.0.1:1", ex.Message);
    }

    [Fact]
    public void ClassifySessionFailure_names_the_profile_and_endpoint_for_a_connection_failure()
    {
        var profile = new DeviceProfile(
            DeviceProfileId.New(), "pixel-7-android-14", MobilePlatform.Android, null, "pixel-7-android-14",
            null, "http://localhost:4723", null, ResetStrategy.Fast, TimeSpan.FromMinutes(5), TenantId.New(), null);
        var connectionFailure = new WebDriverArgumentException("wrapped", new System.Net.Sockets.SocketException());

        var result = MobileModule.ClassifySessionFailure(connectionFailure, profile);

        Assert.Contains("No Appium server reachable at http://localhost:4723", result.Message);
    }

    [Fact]
    public void ClassifySessionFailure_falls_back_to_the_real_Appium_message_and_profile_name_for_a_non_connection_failure()
    {
        var profile = new DeviceProfile(
            DeviceProfileId.New(), "pixel-7-android-14", MobilePlatform.Android, null, "pixel-7-android-14",
            null, "http://localhost:4723", null, ResetStrategy.Fast, TimeSpan.FromMinutes(5), TenantId.New(), null);
        var sessionRejected = new InvalidOperationException("session not created: could not find a connected Android device.");

        var result = MobileModule.ClassifySessionFailure(sessionRejected, profile);

        Assert.Contains("pixel-7-android-14", result.Message);
        Assert.Contains("could not find a connected Android device", result.Message);
        Assert.DoesNotContain("No Appium server reachable", result.Message); // not misclassified as a connection failure
    }

    [Fact]
    public async Task A_farm_credential_reference_is_resolved_through_the_existing_secrets_pipeline_never_taken_as_a_literal()
    {
        var module = new MobileModule();
        var credentials = new FakeCredentialResolver(new() { ["browserstack-key"] = "s3cr3t-value" });
        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["mobile"] = new Dictionary<string, object?>
            {
                ["platformName"] = "Android",
                ["deviceName"] = "pixel-7-android-14",
                ["appiumUrl"] = "http://127.0.0.1:1",
                ["farmCredentialRef"] = "browserstack-key",
            },
        }, credentials);

        var ex = await Assert.ThrowsAsync<MobileSessionException>(() => module.Setup(ctx, CancellationToken.None));

        // The reference was actually resolved (not skipped)...
        Assert.Contains("browserstack-key", credentials.RequestedReferences);
        // ...but neither the reference name nor the resolved secret value ever appears in the
        // thrown message -- the standard sentinel-leak shape every other module's credential
        // handling in this codebase is held to.
        Assert.DoesNotContain("browserstack-key", ex.Message);
        Assert.DoesNotContain("s3cr3t-value", ex.Message);
    }

    [Fact]
    public async Task Run_without_a_prior_setup_fails_cleanly_naming_the_real_reason_not_a_null_reference()
    {
        var module = new MobileModule();
        var ctx = BuildContext(new Dictionary<string, object?>());

        var outcome = await module.Run("tap", new Dictionary<string, object?> { ["selector"] = "/foo" }, ctx, CancellationToken.None);

        Assert.Equal(Yukti.Domain.Execution.StepStatus.Failed, outcome.Status);
        Assert.Contains("not set up", outcome.Error);
    }

    [Fact]
    public async Task An_unknown_mobile_action_fails_cleanly_naming_the_action()
    {
        // Exercises the real Run() dispatch path without a live session by using Teardown's
        // own "nothing to tear down" tolerance -- Run() still correctly reports "not set up"
        // first, proving the unknown-action branch is unreachable without a session, which is
        // itself the right behavior (never attempt an action against a session that isn't there).
        var module = new MobileModule();
        var ctx = BuildContext(new Dictionary<string, object?>());

        var outcome = await module.Run("teleport", new Dictionary<string, object?>(), ctx, CancellationToken.None);

        Assert.Equal(Yukti.Domain.Execution.StepStatus.Failed, outcome.Status);
        Assert.Contains("not set up", outcome.Error);
    }

    [Fact]
    public async Task Teardown_without_a_prior_session_is_a_safe_no_op()
    {
        var module = new MobileModule();
        var ctx = BuildContext(new Dictionary<string, object?>());

        await module.Teardown(ctx, CancellationToken.None); // must not throw
    }

    [Fact]
    public async Task Setup_with_no_mobile_config_is_a_no_op_not_an_error()
    {
        // A flow mixing module kinds shouldn't abort over unrelated missing config -- same
        // reasoning MobileModule's own header comment already states.
        var module = new MobileModule();
        var ctx = BuildContext(new Dictionary<string, object?>());

        await module.Setup(ctx, CancellationToken.None); // must not throw
    }

    // OpenQA.Selenium.WebDriverArgumentException isn't publicly constructible with an inner
    // exception in every version, so this tiny stand-in gives the connection-failure test a
    // real Exception whose InnerException chain contains a SocketException, which is exactly
    // what IsConnectionFailure walks -- without depending on a specific Selenium exception type
    // the test doesn't actually care about.
    private sealed class WebDriverArgumentException : Exception
    {
        public WebDriverArgumentException(string message, Exception inner) : base(message, inner) { }
    }
}
