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
/// API Testing Module Spec, API-T2: pathParams ({placeholder} resolution,
/// encoding, mismatch detection) and queryParams (repeated keys, empty
/// values). {{vars.…}} interpolation itself is FlowEngine/VariableStore's
/// job (VariableStoreTests) — by the time a step's parameters reach
/// ApiModule.Run, any {{vars.…}} reference is already a plain value, so
/// these tests exercise ApiModule directly with already-resolved values.
/// </summary>
public sealed class ApiParametersTests
{
    private static ExecutionContext NewContext() => new()
    {
        RunId = FlowRunId.New(),
        Variables = new Dictionary<string, object?>(),
        Credentials = new InMemoryCredentialResolver(),
        RunCancellation = CancellationToken.None,
    };

    [Fact]
    public async Task PathParams_resolve_placeholders_in_the_url_template()
    {
        string? receivedPath = null;
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            receivedPath = ctx.Request.Url!.AbsolutePath;
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl + "orders/{orderId}/items",
            ["pathParams"] = new Dictionary<string, object?> { ["orderId"] = "123" },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
        Assert.Equal("/orders/123/items", receivedPath);
    }

    [Theory]
    [InlineData("../", "..%2F")]
    [InlineData("?x=1", "%3Fx%3D1")]
    public async Task PathParams_values_cannot_break_out_of_their_segment(string maliciousValue, string expectedEncodedSegment)
    {
        string? receivedPath = null;
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            receivedPath = ctx.Request.RawUrl;
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl + "orders/{orderId}",
            ["pathParams"] = new Dictionary<string, object?> { ["orderId"] = maliciousValue },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
        Assert.Contains(expectedEncodedSegment, receivedPath);
        // The literal, unencoded character never reaches the server as a path/query delimiter.
        Assert.StartsWith("/orders/", receivedPath);
        Assert.DoesNotContain("/orders/../", receivedPath);
    }

    [Fact]
    public async Task Unresolved_placeholder_fails_the_step_before_sending_and_names_it()
    {
        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = "http://127.0.0.1:1/orders/{orderId}",
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Failed, outcome.Status);
        Assert.Contains("orderId", outcome.Error);
    }

    [Fact]
    public async Task Unmatched_pathParam_entry_is_an_error()
    {
        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = "http://127.0.0.1:1/orders",
            ["pathParams"] = new Dictionary<string, object?> { ["orderId"] = "123" },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Failed, outcome.Status);
        Assert.Contains("orderId", outcome.Error);
    }

    [Fact]
    public async Task QueryParams_support_repeated_keys_via_an_array_value()
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
            ["queryParams"] = new Dictionary<string, object?> { ["tag"] = new List<object?> { "a", "b" } },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
        Assert.Equal("tag=a&tag=b", receivedQuery);
    }

    [Fact]
    public async Task QueryParams_support_empty_values()
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
            ["queryParams"] = new Dictionary<string, object?> { ["filter"] = "" },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
        Assert.Equal("filter=", receivedQuery);
    }

    [Fact]
    public async Task QueryParams_replace_the_url_templates_own_value_for_the_same_key()
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
            ["url"] = server.BaseUrl + "?page=1",
            ["queryParams"] = new Dictionary<string, object?> { ["page"] = "2" },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
        Assert.Equal("page=2", receivedQuery);
    }

    [Fact]
    public async Task PathParams_and_queryParams_both_receive_already_resolved_vars_values()
    {
        // ApiModule itself does no {{vars.…}} interpolation — FlowEngine resolves those
        // before dispatch (VariableStore.Interpolate). This proves ApiModule works
        // correctly when handed the kind of plain, already-resolved value that
        // interpolation produces, without needing its own copy of that logic.
        string? receivedPath = null;
        string? receivedQuery = null;
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            receivedPath = ctx.Request.Url!.AbsolutePath;
            receivedQuery = ctx.Request.Url!.Query.TrimStart('?');
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl + "orders/{orderId}",
            ["pathParams"] = new Dictionary<string, object?> { ["orderId"] = "from-vars-123" },
            ["queryParams"] = new Dictionary<string, object?> { ["size"] = "from-vars-10" },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
        Assert.Equal("/orders/from-vars-123", receivedPath);
        Assert.Equal("size=from-vars-10", receivedQuery);
    }
}
