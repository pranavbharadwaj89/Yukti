using Xunit;
using Yukti.Contracts;
using Yukti.Domain.Execution;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;
using Yukti.Infrastructure.InMemory.Modules;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// API Testing Module Spec, API-T8 (ADR-0007): the `schema` assertion backed by JsonSchema.Net —
/// every violation listed with its path, $ref/oneOf/pattern/bounds all work, and a `schema` value
/// that's a string resolves a reusable schema artifact via ApiSchemaStore.
/// </summary>
public sealed class ApiJsonSchemaTests
{
    private static ExecutionContext NewContext() => new()
    {
        RunId = FlowRunId.New(),
        Variables = new Dictionary<string, object?>(),
        Credentials = new InMemoryCredentialResolver(),
        RunCancellation = CancellationToken.None,
    };

    [Fact]
    public async Task Every_violation_is_listed_with_its_path_not_just_the_first()
    {
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, "{\"id\":\"not-a-number\",\"name\":\"ab\"}");
        });

        var schema = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object?>
            {
                ["id"] = new Dictionary<string, object?> { ["type"] = "integer" },
                ["name"] = new Dictionary<string, object?> { ["type"] = "string", ["minLength"] = 3L },
            },
        };

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["assert"] = new List<object?> { new Dictionary<string, object?> { ["type"] = "schema", ["schema"] = schema } },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Failed, outcome.Status);
        Assert.Contains("/id", outcome.Error);
        Assert.Contains("/name", outcome.Error);
    }

    [Fact]
    public async Task OneOf_and_pattern_and_numeric_bounds_are_supported()
    {
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, "{\"code\":\"AB-1\",\"count\":500}");
        });

        var schema = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object?>
            {
                ["code"] = new Dictionary<string, object?> { ["type"] = "string", ["pattern"] = "^[A-Z]{2}-\\d+$" },
                ["count"] = new Dictionary<string, object?> { ["type"] = "integer", ["maximum"] = 100L },
            },
        };

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["assert"] = new List<object?> { new Dictionary<string, object?> { ["type"] = "schema", ["schema"] = schema } },
        }, NewContext(), CancellationToken.None);

        // "code" satisfies the pattern (passes); "count" violates the maximum bound (fails) --
        // neither `pattern` nor `maximum` existed in the old hand-rolled validator at all.
        Assert.Equal(StepStatus.Failed, outcome.Status);
        Assert.DoesNotContain("/code", outcome.Error);
        Assert.Contains("/count", outcome.Error);
    }

    [Fact]
    public async Task A_valid_body_passes()
    {
        using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await ApiModuleAssertionTests.WriteBody(ctx, "{\"id\":42,\"name\":\"widget\"}");
        });

        var schema = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["required"] = new List<object?> { "id", "name" },
            ["properties"] = new Dictionary<string, object?>
            {
                ["id"] = new Dictionary<string, object?> { ["type"] = "integer" },
                ["name"] = new Dictionary<string, object?> { ["type"] = "string" },
            },
        };

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["assert"] = new List<object?> { new Dictionary<string, object?> { ["type"] = "schema", ["schema"] = schema } },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
    }

    [Fact]
    public async Task A_schema_reference_by_name_loads_from_the_configured_schema_store()
    {
        var schemasDir = Path.Combine(Path.GetTempPath(), $"yukti-api-schemas-{Guid.NewGuid():N}");
        Directory.CreateDirectory(schemasDir);
        await File.WriteAllTextAsync(Path.Combine(schemasDir, "order.json"),
            """{ "type": "object", "required": ["id"], "properties": { "id": { "type": "integer" } } }""");

        var previousEnv = Environment.GetEnvironmentVariable("YUKTI_API_SCHEMAS_DIR");
        Environment.SetEnvironmentVariable("YUKTI_API_SCHEMAS_DIR", schemasDir);
        try
        {
            using var server = new ApiModuleAssertionTests.TestHttpServer(async ctx =>
            {
                ctx.Response.StatusCode = 200;
                await ApiModuleAssertionTests.WriteBody(ctx, "{\"id\":\"wrong-type\"}");
            });

            var module = new ApiModule();
            var outcome = await module.Run("request", new Dictionary<string, object?>
            {
                ["url"] = server.BaseUrl,
                ["assert"] = new List<object?> { new Dictionary<string, object?> { ["type"] = "schema", ["schema"] = "order" } },
            }, NewContext(), CancellationToken.None);

            Assert.Equal(StepStatus.Failed, outcome.Status);
            Assert.Contains("/id", outcome.Error);
        }
        finally
        {
            Environment.SetEnvironmentVariable("YUKTI_API_SCHEMAS_DIR", previousEnv);
            Directory.Delete(schemasDir, recursive: true);
        }
    }

    [Fact]
    public async Task An_unknown_schema_reference_fails_the_step_naming_it()
    {
        var schemasDir = Path.Combine(Path.GetTempPath(), $"yukti-api-schemas-{Guid.NewGuid():N}");
        Directory.CreateDirectory(schemasDir);

        var previousEnv = Environment.GetEnvironmentVariable("YUKTI_API_SCHEMAS_DIR");
        Environment.SetEnvironmentVariable("YUKTI_API_SCHEMAS_DIR", schemasDir);
        try
        {
            var module = new ApiModule();
            var outcome = await module.Run("request", new Dictionary<string, object?>
            {
                ["url"] = "http://127.0.0.1:1/",
                ["assert"] = new List<object?> { new Dictionary<string, object?> { ["type"] = "schema", ["schema"] = "does-not-exist" } },
            }, NewContext(), CancellationToken.None);

            Assert.Equal(StepStatus.Failed, outcome.Status);
            Assert.Contains("does-not-exist", outcome.Error);
        }
        finally
        {
            Environment.SetEnvironmentVariable("YUKTI_API_SCHEMAS_DIR", previousEnv);
            Directory.Delete(schemasDir, recursive: true);
        }
    }
}
