using Xunit;
using Yukti.Contracts;
using Yukti.Domain.Execution;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;
using Yukti.Infrastructure.InMemory.Modules;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// API Testing Module Spec, API-T4: Basic/Bearer/API key auth. By the time ApiModule.Run sees
/// `auth`, FlowEngine has already resolved any {{secrets.name}} reference inside it to a real
/// value (VariableStore, API-T4) — these tests exercise ApiModule directly with already-resolved
/// values, same convention as ApiParametersTests/ApiCookieTests. The {{secrets.name}} resolution
/// itself is proven in VariableStoreTests; the leak/redaction proof is in
/// Yukti.Infrastructure.Tests.SecretRedactionTests (reusing Day 6's sentinel scan, as the spec
/// requires, not a second one).
/// </summary>
public sealed class ApiAuthTests
{
    private static ExecutionContext NewContext() => new()
    {
        RunId = FlowRunId.New(),
        Variables = new Dictionary<string, object?>(),
        Credentials = new InMemoryCredentialResolver(),
        RunCancellation = CancellationToken.None,
    };

    [Fact]
    public async Task Basic_auth_sets_a_base64_authorization_header()
    {
        string? receivedAuth = null;
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            receivedAuth = ctx.Request.Headers["Authorization"];
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["auth"] = new Dictionary<string, object?> { ["type"] = "basic", ["username"] = "alice", ["password"] = "s3cret" },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
        var expected = "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("alice:s3cret"));
        Assert.Equal(expected, receivedAuth);
    }

    [Fact]
    public async Task Bearer_auth_sets_the_authorization_header()
    {
        string? receivedAuth = null;
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            receivedAuth = ctx.Request.Headers["Authorization"];
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["auth"] = new Dictionary<string, object?> { ["type"] = "bearer", ["token"] = "tok-abc123" },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
        Assert.Equal("Bearer tok-abc123", receivedAuth);
    }

    [Fact]
    public async Task ApiKey_auth_can_be_placed_in_a_header()
    {
        string? receivedKey = null;
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            receivedKey = ctx.Request.Headers["X-Api-Key"];
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["auth"] = new Dictionary<string, object?> { ["type"] = "apiKey", ["name"] = "X-Api-Key", ["value"] = "key-xyz", ["in"] = "header" },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
        Assert.Equal("key-xyz", receivedKey);
    }

    [Fact]
    public async Task ApiKey_auth_can_be_placed_in_a_query_parameter()
    {
        string? receivedQuery = null;
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            receivedQuery = ctx.Request.Url!.Query.TrimStart('?');
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["auth"] = new Dictionary<string, object?> { ["type"] = "apiKey", ["name"] = "api_key", ["value"] = "key-xyz", ["in"] = "query" },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
        Assert.Equal("api_key=key-xyz", receivedQuery);
    }

    [Fact]
    public async Task ApiKey_auth_can_be_placed_in_a_cookie()
    {
        string? receivedCookie = null;
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            receivedCookie = ctx.Request.Headers["Cookie"];
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["auth"] = new Dictionary<string, object?> { ["type"] = "apiKey", ["name"] = "session", ["value"] = "key-xyz", ["in"] = "cookie" },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
        Assert.Contains("session=key-xyz", receivedCookie);
    }

    [Fact]
    public async Task An_explicit_header_of_the_same_name_overrides_what_auth_would_set()
    {
        string? receivedAuth = null;
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            receivedAuth = ctx.Request.Headers["Authorization"];
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["auth"] = new Dictionary<string, object?> { ["type"] = "bearer", ["token"] = "tok-abc123" },
            ["headers"] = new Dictionary<string, object?> { ["Authorization"] = "Bearer explicit-override" },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
        Assert.Equal("Bearer explicit-override", receivedAuth);
    }

    [Fact]
    public async Task An_unknown_auth_type_fails_the_step_cleanly()
    {
        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = "http://127.0.0.1:1/",
            ["auth"] = new Dictionary<string, object?> { ["type"] = "digest" },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Failed, outcome.Status);
        Assert.Contains("digest", outcome.Error);
    }

    [Fact]
    public async Task A_missing_required_auth_field_fails_the_step_cleanly()
    {
        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = "http://127.0.0.1:1/",
            ["auth"] = new Dictionary<string, object?> { ["type"] = "bearer" }, // missing "token"
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Failed, outcome.Status);
        Assert.Contains("token", outcome.Error);
    }
}
