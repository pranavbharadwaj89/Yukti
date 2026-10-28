using System.Net;
using Xunit;
using Yukti.Contracts;
using Yukti.Domain.Execution;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;
using Yukti.Infrastructure.InMemory.Modules;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// API Testing Module Spec, API-T5: OAuth 2.0 client credentials — ApiModule fetches its own
/// token (through its own SSRF-guarded HttpClient, same as any other request), caches it for its
/// lifetime minus a safety margin, and the cache is tenant-isolated. The custom-auth escape
/// hatch (call an endpoint, extract a value with API-T6, use it via {{vars}}) needs no test here
/// — it's ordinary, already-covered flow authoring (ApiExtractionTests), not new module code.
/// </summary>
public sealed class ApiOAuthTests
{
    private static ExecutionContext NewContext(TenantId? tenantId = null) => new()
    {
        RunId = FlowRunId.New(),
        Variables = new Dictionary<string, object?>(),
        Credentials = new InMemoryCredentialResolver(),
        RunCancellation = CancellationToken.None,
        TenantId = tenantId,
    };

    /// <summary>A minimal OAuth2 token endpoint: counts requests, returns a token with a
    /// caller-controlled expires_in so tests can force expiry, or a 400 "invalid_client" error
    /// when configured to fail.</summary>
    private sealed class StubTokenServer : IDisposable
    {
        private readonly ApiModuleAssertionTests.TestHttpServer _server;
        public int RequestCount;
        public int ExpiresInSeconds = 3600;
        public bool FailWithInvalidClient;
        public string BaseUrl => _server.BaseUrl;

        public StubTokenServer()
        {
            _server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
            {
                Interlocked.Increment(ref RequestCount);
                if (FailWithInvalidClient)
                {
                    ctx.Response.StatusCode = 400;
                    await ApiModuleAssertionTests.WriteBody(ctx, """{"error":"invalid_client","error_description":"Unknown client_id"}""");
                    return;
                }
                ctx.Response.StatusCode = 200;
                await ApiModuleAssertionTests.WriteBody(ctx, $$"""{"access_token":"tok-{{RequestCount}}","expires_in":{{ExpiresInSeconds}},"token_type":"Bearer"}""");
            });
        }

        public void Dispose() => _server.Dispose();
    }

    [Fact]
    public async Task Fetches_a_token_and_attaches_it_as_bearer()
    {
        using var tokenServer = new StubTokenServer();
        string? receivedAuth = null;
        using var apiServer = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            receivedAuth = ctx.Request.Headers["Authorization"];
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = apiServer.BaseUrl,
            ["auth"] = new Dictionary<string, object?>
            {
                ["type"] = "oauth2ClientCredentials",
                ["tokenUrl"] = tokenServer.BaseUrl,
                ["clientId"] = "client-1",
                ["clientSecret"] = "secret-1",
            },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
        Assert.Equal("Bearer tok-1", receivedAuth);
        Assert.Equal(1, tokenServer.RequestCount);
    }

    [Fact]
    public async Task The_token_request_itself_sends_the_expected_client_credentials_grant()
    {
        string? receivedBody = null;
        string? receivedContentType = null;
        using var tokenServer = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            receivedContentType = ctx.Request.ContentType;
            using var reader = new StreamReader(ctx.Request.InputStream);
            receivedBody = await reader.ReadToEndAsync();
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, """{"access_token":"tok-x","expires_in":3600}""");
        });
        using var apiServer = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = apiServer.BaseUrl,
            ["auth"] = new Dictionary<string, object?>
            {
                ["type"] = "oauth2ClientCredentials",
                ["tokenUrl"] = tokenServer.BaseUrl,
                ["clientId"] = "client-2",
                ["clientSecret"] = "secret-2",
                ["scopes"] = new List<object?> { "read", "write" },
            },
        }, NewContext(), CancellationToken.None);

        Assert.StartsWith("application/x-www-form-urlencoded", receivedContentType);
        Assert.Contains("grant_type=client_credentials", receivedBody);
        Assert.Contains("client_id=client-2", receivedBody);
        Assert.Contains("client_secret=secret-2", receivedBody);
        Assert.Contains("scope=read+write", receivedBody);
    }

    [Fact]
    public async Task A_token_is_reused_across_two_steps_without_a_second_fetch()
    {
        using var tokenServer = new StubTokenServer();
        using var apiServer = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var authParams = new Dictionary<string, object?>
        {
            ["url"] = apiServer.BaseUrl,
            ["auth"] = new Dictionary<string, object?>
            {
                ["type"] = "oauth2ClientCredentials",
                ["tokenUrl"] = tokenServer.BaseUrl,
                ["clientId"] = "client-reuse",
                ["clientSecret"] = "secret-reuse",
            },
        };
        var ctx = NewContext();

        var first = await module.Run("request", authParams, ctx, CancellationToken.None);
        var second = await module.Run("request", authParams, ctx, CancellationToken.None);

        Assert.Equal(StepStatus.Passed, first.Status);
        Assert.Equal(StepStatus.Passed, second.Status);
        Assert.Equal(1, tokenServer.RequestCount);
    }

    [Fact]
    public async Task An_expired_token_is_refetched_transparently()
    {
        using var tokenServer = new StubTokenServer { ExpiresInSeconds = 60 }; // minus the 60s margin -> expires immediately
        using var apiServer = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var authParams = new Dictionary<string, object?>
        {
            ["url"] = apiServer.BaseUrl,
            ["auth"] = new Dictionary<string, object?>
            {
                ["type"] = "oauth2ClientCredentials",
                ["tokenUrl"] = tokenServer.BaseUrl,
                ["clientId"] = "client-expiry",
                ["clientSecret"] = "secret-expiry",
            },
        };
        var ctx = NewContext();

        await module.Run("request", authParams, ctx, CancellationToken.None);
        await Task.Delay(50); // make sure "now" has actually moved past the already-expired cache entry
        await module.Run("request", authParams, ctx, CancellationToken.None);

        Assert.Equal(2, tokenServer.RequestCount);
    }

    [Fact]
    public async Task A_token_fetch_failure_fails_the_step_with_the_providers_error_description()
    {
        using var tokenServer = new StubTokenServer { FailWithInvalidClient = true };

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = "http://127.0.0.1:1/",
            ["auth"] = new Dictionary<string, object?>
            {
                ["type"] = "oauth2ClientCredentials",
                ["tokenUrl"] = tokenServer.BaseUrl,
                ["clientId"] = "bad-client",
                ["clientSecret"] = "bad-secret",
            },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Failed, outcome.Status);
        Assert.Contains("Unknown client_id", outcome.Error);
    }

    [Fact]
    public async Task Two_tenants_with_the_same_token_url_and_client_never_share_a_cached_token()
    {
        using var tokenServer = new StubTokenServer();
        using var apiServer = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var authParams = new Dictionary<string, object?>
        {
            ["url"] = apiServer.BaseUrl,
            ["auth"] = new Dictionary<string, object?>
            {
                ["type"] = "oauth2ClientCredentials",
                ["tokenUrl"] = tokenServer.BaseUrl,
                // Same tokenUrl AND clientId on purpose -- isolation must come from the tenant
                // alone, not from authors happening to use different client credentials per tenant.
                ["clientId"] = "shared-client-id",
                ["clientSecret"] = "shared-secret",
            },
        };

        var tenantA = NewContext(TenantId.New());
        var tenantB = NewContext(TenantId.New());

        await module.Run("request", authParams, tenantA, CancellationToken.None);
        await module.Run("request", authParams, tenantB, CancellationToken.None);

        Assert.Equal(2, tokenServer.RequestCount);
    }
}
