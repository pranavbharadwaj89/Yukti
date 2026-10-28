using System.Net;
using Xunit;
using Yukti.Application.Security;
using Yukti.Contracts;
using Yukti.Domain.Execution;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;
using Yukti.Infrastructure.InMemory.Modules;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// API Testing Module Spec, API-T3: the cookie jar (set, persist across steps in one run,
/// isolated across runs) and redirect policy (follow/none/sameHost, every hop through the SSRF
/// guard). Body-encoding coverage lives in ApiBodyTests.
/// </summary>
public sealed class ApiCookieTests
{
    private static ExecutionContext NewContext(FlowRunId? runId = null) => new()
    {
        RunId = runId ?? FlowRunId.New(),
        Variables = new Dictionary<string, object?>(),
        Credentials = new InMemoryCredentialResolver(),
        RunCancellation = CancellationToken.None,
    };

    /// <summary>Allows every host except one specific denied host/IP literal — lets a test
    /// exercise "the real local TestHttpServer is fine, but the redirect target is blocked"
    /// without a real private-network dependency.</summary>
    private sealed class DenyHostGuard : IOutboundGuard
    {
        private readonly string _deniedHost;
        public DenyHostGuard(string deniedHost) => _deniedHost = deniedHost;

        public Task<IReadOnlyList<IPAddress>> ResolveAllowedAddressesAsync(string host, Guid? tenantId, CancellationToken ct)
        {
            if (string.Equals(host, _deniedHost, StringComparison.OrdinalIgnoreCase))
                throw new BlockedTargetException(host, IPAddress.Parse("169.254.169.254"));
            return PermissiveOutboundGuard.Instance.ResolveAllowedAddressesAsync(host, tenantId, ct);
        }
    }

    [Fact]
    public async Task Cookies_set_on_a_step_are_sent_to_the_server()
    {
        string? receivedCookieHeader = null;
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            receivedCookieHeader = ctx.Request.Headers["Cookie"];
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["cookies"] = new Dictionary<string, object?> { ["session"] = "abc123" },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
        Assert.Contains("session=abc123", receivedCookieHeader);
    }

    [Fact]
    public async Task A_cookie_set_by_one_step_is_sent_automatically_by_a_later_step_in_the_same_run()
    {
        var requestNumber = 0;
        string? secondRequestCookieHeader = null;
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            requestNumber++;
            if (requestNumber == 1)
            {
                ctx.Response.Headers.Add("Set-Cookie", "session=from-login; Path=/");
            }
            else
            {
                secondRequestCookieHeader = ctx.Request.Headers["Cookie"];
            }
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var ctx = NewContext();
        await module.Setup(ctx, CancellationToken.None);
        try
        {
            var first = await module.Run("request", new Dictionary<string, object?> { ["url"] = server.BaseUrl }, ctx, CancellationToken.None);
            Assert.Equal(StepStatus.Passed, first.Status);

            var second = await module.Run("request", new Dictionary<string, object?> { ["url"] = server.BaseUrl }, ctx, CancellationToken.None);
            Assert.Equal(StepStatus.Passed, second.Status);
        }
        finally
        {
            await module.Teardown(ctx, CancellationToken.None);
        }

        Assert.Contains("session=from-login", secondRequestCookieHeader);
    }

    [Fact]
    public async Task The_cookie_jar_is_isolated_between_two_concurrent_runs()
    {
        var sawCookieOnSecondRun = false;
        var requestNumber = 0;
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            requestNumber++;
            if (requestNumber == 1)
            {
                ctx.Response.Headers.Add("Set-Cookie", "session=run-a-only; Path=/");
            }
            else if (!string.IsNullOrEmpty(ctx.Request.Headers["Cookie"]))
            {
                sawCookieOnSecondRun = true;
            }
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var runA = NewContext();
        var runB = NewContext();
        await module.Setup(runA, CancellationToken.None);
        await module.Setup(runB, CancellationToken.None);
        try
        {
            var first = await module.Run("request", new Dictionary<string, object?> { ["url"] = server.BaseUrl }, runA, CancellationToken.None);
            Assert.Equal(StepStatus.Passed, first.Status);

            var second = await module.Run("request", new Dictionary<string, object?> { ["url"] = server.BaseUrl }, runB, CancellationToken.None);
            Assert.Equal(StepStatus.Passed, second.Status);
        }
        finally
        {
            await module.Teardown(runA, CancellationToken.None);
            await module.Teardown(runB, CancellationToken.None);
        }

        Assert.False(sawCookieOnSecondRun, "run B must never see a cookie run A's response set.");
    }

    [Fact]
    public async Task Redirect_is_followed_by_default_and_lands_on_the_final_response()
    {
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            if (ctx.Request.Url!.AbsolutePath == "/start")
            {
                ctx.Response.StatusCode = 302;
                ctx.Response.Headers.Add("Location", "/final");
                ctx.Response.Close();
            }
            else
            {
                ctx.Response.StatusCode = 200;
                await ApiModuleAssertionTests.WriteBody(ctx, "{\"landed\":true}");
            }
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl + "start",
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
        var data = Assert.IsType<Dictionary<string, object?>>(outcome.Data);
        Assert.Equal(200L, Convert.ToInt64(data["status"]));
    }

    [Fact]
    public async Task RedirectPolicy_none_stops_at_the_redirect_response_itself()
    {
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            if (ctx.Request.Url!.AbsolutePath == "/start")
            {
                ctx.Response.StatusCode = 302;
                ctx.Response.Headers.Add("Location", "/final");
                ctx.Response.Close();
            }
            else
            {
                ctx.Response.StatusCode = 200;
                await ApiModuleAssertionTests.WriteBody(ctx, "{\"landed\":true}");
            }
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl + "start",
            ["redirectPolicy"] = "none",
        }, NewContext(), CancellationToken.None);

        var data = Assert.IsType<Dictionary<string, object?>>(outcome.Data);
        Assert.Equal(302L, Convert.ToInt64(data["status"]));
    }

    [Fact]
    public async Task RedirectPolicy_sameHost_does_not_follow_a_redirect_to_a_different_host()
    {
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 302;
            ctx.Response.Headers.Add("Location", "http://198.51.100.1:9999/elsewhere");
            ctx.Response.Close();
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["redirectPolicy"] = "sameHost",
        }, NewContext(), CancellationToken.None);

        var data = Assert.IsType<Dictionary<string, object?>>(outcome.Data);
        Assert.Equal(302L, Convert.ToInt64(data["status"]));
    }

    [Fact]
    public async Task A_redirect_to_a_blocked_address_fails_the_step_through_the_SSRF_guard()
    {
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 302;
            ctx.Response.Headers.Add("Location", "http://169.254.169.254/");
            ctx.Response.Close();
        });

        var module = new ApiModule(new DenyHostGuard("169.254.169.254"));
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Failed, outcome.Status);
    }
}
