using Xunit;
using Yukti.Contracts;
using Yukti.Domain.Execution;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;
using Yukti.Infrastructure.InMemory.Modules;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// API Testing Module Spec, API-T6: `extract` writes named values into this step's own
/// `data.extracted.<saveAs>` — reached via {{vars.&lt;step&gt;.extracted.&lt;saveAs&gt;}}, not a
/// flat top-level {{vars.&lt;saveAs&gt;}}. See ApiModule.BuildExtractions' own doc comment for why
/// that satisfies "reuse the existing validator" with zero engine/domain change.
/// </summary>
public sealed class ApiExtractionTests
{
    private static ExecutionContext NewContext() => new()
    {
        RunId = FlowRunId.New(),
        Variables = new Dictionary<string, object?>(),
        Credentials = new InMemoryCredentialResolver(),
        RunCancellation = CancellationToken.None,
    };

    private static Dictionary<string, object?> Extracted(StepOutcome outcome)
    {
        var data = Assert.IsType<Dictionary<string, object?>>(outcome.Data);
        return Assert.IsType<Dictionary<string, object?>>(data["extracted"]);
    }

    [Fact]
    public async Task Extracts_a_JsonPath_value_from_the_body_keeping_its_type()
    {
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, "{\"data\":{\"orders\":[{\"id\":42}]}}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["extract"] = new List<object?>
            {
                new Dictionary<string, object?> { ["saveAs"] = "orderId", ["source"] = "body", ["path"] = "data.orders[0].id" },
            },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
        var extracted = Extracted(outcome);
        // JsonPathEvaluator.ToPlainValue's own ternary widens to double for any JSON number
        // (existing, pre-API-T6 behavior) -- compare by value, not by exact boxed CLR type,
        // same as PlainValueEquality already does for assertions.
        Assert.Equal(42d, Convert.ToDouble(extracted["orderId"]));
    }

    [Fact]
    public async Task Extracts_from_header_status_and_response_time_in_one_step()
    {
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 201;
            ctx.Response.Headers.Add("X-Request-Id", "req-abc");
            await ApiModuleAssertionTests.WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["extract"] = new List<object?>
            {
                new Dictionary<string, object?> { ["saveAs"] = "requestId", ["source"] = "header", ["path"] = "X-Request-Id" },
                new Dictionary<string, object?> { ["saveAs"] = "code", ["source"] = "status" },
                new Dictionary<string, object?> { ["saveAs"] = "elapsed", ["source"] = "responseTime" },
            },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
        var extracted = Extracted(outcome);
        Assert.Equal("req-abc", extracted["requestId"]);
        Assert.Equal(201L, extracted["code"]);
        Assert.True(Convert.ToInt64(extracted["elapsed"]) >= 0);
    }

    [Fact]
    public async Task Extracts_a_cookie_value_set_by_the_same_response()
    {
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.Headers.Add("Set-Cookie", "session=xyz789; Path=/");
            await ApiModuleAssertionTests.WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["extract"] = new List<object?>
            {
                new Dictionary<string, object?> { ["saveAs"] = "sessionCookie", ["source"] = "cookie", ["path"] = "session" },
            },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
        Assert.Equal("xyz789", Extracted(outcome)["sessionCookie"]);
    }

    [Fact]
    public async Task A_path_matching_nothing_fails_the_step_naming_the_path()
    {
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, "{\"data\":{\"id\":1}}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["extract"] = new List<object?>
            {
                new Dictionary<string, object?> { ["saveAs"] = "missing", ["source"] = "body", ["path"] = "data.nope" },
            },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Failed, outcome.Status);
        Assert.Contains("missing", outcome.Error);
        Assert.Contains("data.nope", outcome.Error);
    }

    [Fact]
    public async Task Required_false_writes_null_instead_of_failing_when_the_path_is_absent()
    {
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, "{\"data\":{\"id\":1}}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["extract"] = new List<object?>
            {
                new Dictionary<string, object?> { ["saveAs"] = "optional", ["source"] = "body", ["path"] = "data.nope", ["required"] = false },
            },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
        Assert.Null(Extracted(outcome)["optional"]);
    }

    [Fact]
    public async Task Extracts_an_object_value_keeping_its_shape_for_later_dotted_access()
    {
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, "{\"order\":{\"customer\":{\"name\":\"Ada\"}}}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["extract"] = new List<object?>
            {
                new Dictionary<string, object?> { ["saveAs"] = "order", ["source"] = "body", ["path"] = "order" },
            },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
        var order = Assert.IsType<Dictionary<string, object?>>(Extracted(outcome)["order"]);
        var customer = Assert.IsType<Dictionary<string, object?>>(order["customer"]);
        Assert.Equal("Ada", customer["name"]);
    }

    [Fact]
    public async Task XPath_extracts_a_value_from_an_xml_response()
    {
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/xml";
            await ApiModuleAssertionTests.WriteBody(ctx, "<order><id>99</id></order>");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["extract"] = new List<object?>
            {
                new Dictionary<string, object?> { ["saveAs"] = "orderId", ["source"] = "body", ["xpath"] = "/order/id" },
            },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
        Assert.Equal("99", Extracted(outcome)["orderId"]);
    }

    [Fact]
    public async Task XPath_matching_nothing_fails_the_step_by_default()
    {
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/xml";
            await ApiModuleAssertionTests.WriteBody(ctx, "<order><id>99</id></order>");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["extract"] = new List<object?>
            {
                new Dictionary<string, object?> { ["saveAs"] = "missing", ["source"] = "body", ["xpath"] = "/order/nope" },
            },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Failed, outcome.Status);
        Assert.Contains("missing", outcome.Error);
    }

    [Fact]
    public async Task Several_extractions_in_one_step_each_keep_their_own_saveAs()
    {
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, "{\"id\":1,\"name\":\"widget\"}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["extract"] = new List<object?>
            {
                new Dictionary<string, object?> { ["saveAs"] = "id", ["source"] = "body", ["path"] = "id" },
                new Dictionary<string, object?> { ["saveAs"] = "name", ["source"] = "body", ["path"] = "name" },
            },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
        var extracted = Extracted(outcome);
        Assert.Equal(1d, Convert.ToDouble(extracted["id"]));
        Assert.Equal("widget", extracted["name"]);
    }
}
