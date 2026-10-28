using System.Net;
using System.Net.Sockets;
using Xunit;
using Yukti.Contracts;
using Yukti.Domain.Execution;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;
using Yukti.Infrastructure.InMemory.Modules;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// End-to-end coverage of ApiModule's headers/body/queryParams/timeoutMs/
/// assert extensions against a real local HTTP server — deliberately
/// integration-style rather than unit-testing JsonPathEvaluator/
/// AssertionEvaluator/AssertionParamMapper in isolation, since those types
/// are internal to Yukti.Infrastructure.InMemory and this project has no
/// existing InternalsVisibleTo precedent to introduce just for this.
/// </summary>
public sealed class ApiModuleAssertionTests
{
    private static ExecutionContext NewContext() => new()
    {
        RunId = FlowRunId.New(),
        Variables = new Dictionary<string, object?>(),
        Credentials = new InMemoryCredentialResolver(),
        RunCancellation = CancellationToken.None,
    };

    [Fact]
    public async Task Headers_and_query_params_reach_the_server()
    {
        HttpListenerContext? received = null;
        using var server = new TestHttpServer(async ctx =>
        {
            received = ctx;
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await WriteBody(ctx, "{\"ok\":true}");
        });

        var module = new ApiModule();
        var parameters = new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl + "widgets",
            ["method"] = "GET",
            ["headers"] = new Dictionary<string, object?> { ["X-Test-Header"] = "abc123" },
            ["queryParams"] = new Dictionary<string, object?> { ["page"] = "2", ["size"] = "10" },
        };

        var outcome = await module.Run("request", parameters, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
        Assert.NotNull(received);
        Assert.Equal("abc123", received!.Request.Headers["X-Test-Header"]);
        Assert.Equal("/widgets", received.Request.Url!.AbsolutePath);
        Assert.Equal("page=2&size=10", received.Request.Url!.Query.TrimStart('?'));
    }

    [Fact]
    public async Task Object_body_is_sent_as_json_and_string_body_as_text_plain()
    {
        string? receivedContentType = null;
        string? receivedBody = null;
        using var server = new TestHttpServer(async ctx =>
        {
            receivedContentType = ctx.Request.ContentType;
            using var reader = new StreamReader(ctx.Request.InputStream);
            receivedBody = await reader.ReadToEndAsync();
            ctx.Response.StatusCode = 200;
            await WriteBody(ctx, "{}");
        });

        var module = new ApiModule();

        await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["method"] = "POST",
            ["body"] = new Dictionary<string, object?> { ["name"] = "widget" },
        }, NewContext(), CancellationToken.None);

        Assert.StartsWith("application/json", receivedContentType);
        Assert.Contains("\"name\"", receivedBody);

        await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["method"] = "POST",
            ["body"] = "plain text payload",
        }, NewContext(), CancellationToken.None);

        Assert.StartsWith("text/plain", receivedContentType);
        Assert.Equal("plain text payload", receivedBody);
    }

    [Fact]
    public async Task Timeout_produces_a_distinct_error_message_not_a_generic_cancellation_message()
    {
        using var server = new TestHttpServer(async ctx =>
        {
            await Task.Delay(2000);
            ctx.Response.StatusCode = 200;
            await WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["timeoutMs"] = 100,
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Failed, outcome.Status);
        Assert.Equal("Request timed out after 100ms", outcome.Error);
    }

    [Fact]
    public async Task Assert_array_evaluates_every_entry_and_does_not_fail_fast()
    {
        using var server = new TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await WriteBody(ctx, "{\"id\":41,\"items\":[\"a\",\"b\"]}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["assert"] = new List<object?>
            {
                new Dictionary<string, object?> { ["type"] = "status", ["expectedStatus"] = 200 }, // passes
                new Dictionary<string, object?> { ["type"] = "pathEquals", ["path"] = "id", ["equals"] = 42L }, // fails: actual is 41
                new Dictionary<string, object?> { ["type"] = "pathContains", ["path"] = "items", ["contains"] = "zzz" }, // fails: not present
            },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Failed, outcome.Status);
        Assert.Contains("expected 42, got 41", outcome.Error);
        Assert.Contains("does not contain", outcome.Error);
        Assert.DoesNotContain("Expected status 200", outcome.Error); // the passing status assertion must not appear in the failure list
    }

    [Fact]
    public async Task HeaderExists_and_CookieExists_pass_and_fail_correctly()
    {
        using var server = new TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.Headers.Add("X-Request-Id", "abc-123");
            ctx.Response.Headers.Add("Set-Cookie", "session=xyz; Path=/; HttpOnly");
            await WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["assert"] = new List<object?>
            {
                new Dictionary<string, object?> { ["type"] = "headerExists", ["header"] = "x-request-id" }, // passes, case-insensitive
                new Dictionary<string, object?> { ["type"] = "cookieExists", ["cookie"] = "session" }, // passes
                new Dictionary<string, object?> { ["type"] = "headerExists", ["header"] = "X-Does-Not-Exist" }, // fails
                new Dictionary<string, object?> { ["type"] = "cookieExists", ["cookie"] = "does-not-exist" }, // fails
            },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Failed, outcome.Status);
        Assert.Contains("Header 'X-Does-Not-Exist' does not exist", outcome.Error);
        Assert.Contains("Cookie 'does-not-exist' does not exist", outcome.Error);
        Assert.DoesNotContain("'x-request-id' does not exist", outcome.Error);
        Assert.DoesNotContain("'session' does not exist", outcome.Error);
    }

    [Fact]
    public async Task Schema_assertion_validates_type_required_properties_items_and_enum()
    {
        using var server = new TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await WriteBody(ctx, "{\"id\":42,\"status\":\"active\",\"tags\":[\"a\",\"b\"]}");
        });

        var module = new ApiModule();
        var schema = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["required"] = new List<object?> { "id", "status", "missingField" },
            ["properties"] = new Dictionary<string, object?>
            {
                ["id"] = new Dictionary<string, object?> { ["type"] = "number" },
                ["status"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = new List<object?> { "active", "inactive" } },
                ["tags"] = new Dictionary<string, object?> { ["type"] = "array", ["items"] = new Dictionary<string, object?> { ["type"] = "string" } },
            },
        };

        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["assert"] = new List<object?> { new Dictionary<string, object?> { ["type"] = "schema", ["schema"] = schema } },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Failed, outcome.Status);
        // API-T8 (ADR-0007): schema validation moved to JsonSchema.Net — exact wording is the
        // library's own, so this checks the missing field is named, not an exact phrase.
        Assert.Contains("missingField", outcome.Error);
    }

    [Fact]
    public async Task Schema_assertion_passes_for_a_valid_body()
    {
        using var server = new TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await WriteBody(ctx, "{\"id\":42,\"status\":\"active\"}");
        });

        var module = new ApiModule();
        var schema = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["required"] = new List<object?> { "id", "status" },
            ["properties"] = new Dictionary<string, object?>
            {
                ["id"] = new Dictionary<string, object?> { ["type"] = "number" },
                ["status"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = new List<object?> { "active", "inactive" } },
            },
        };

        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["assert"] = new List<object?> { new Dictionary<string, object?> { ["type"] = "schema", ["schema"] = schema } },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
    }

    [Fact]
    public async Task Standalone_expectedStatus_still_works_unchanged()
    {
        using var server = new TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 201;
            await WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["expectedStatus"] = 200L,
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Failed, outcome.Status);
        Assert.Equal("Expected status 200, got 201", outcome.Error);
    }

    [Fact]
    public async Task Malformed_assert_entry_becomes_a_clean_failed_outcome_not_an_unhandled_exception()
    {
        using var server = new TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["assert"] = new List<object?>
            {
                new Dictionary<string, object?> { ["type"] = "totallyUnknownType" },
            },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Failed, outcome.Status);
        Assert.Contains("Unknown assertion type", outcome.Error);
    }

    [Fact]
    public async Task Missing_url_becomes_a_clean_failed_outcome_not_an_unhandled_exception()
    {
        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>(), NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Failed, outcome.Status);
        Assert.Contains("requires a 'url' parameter", outcome.Error);
    }

    // --- API Testing Module Spec, API-T7: the expanded assertion set ---

    [Fact]
    public async Task StatusIn_passes_when_the_actual_status_is_one_of_the_allowed_set()
    {
        using var server = new TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 201;
            await WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["assert"] = new List<object?> { new Dictionary<string, object?> { ["type"] = "statusIn", ["expectedStatuses"] = new List<object?> { 200L, 201L, 202L } } },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
    }

    [Fact]
    public async Task StatusIn_fails_naming_the_allowed_set_when_the_actual_status_is_outside_it()
    {
        using var server = new TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 500;
            await WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["assert"] = new List<object?> { new Dictionary<string, object?> { ["type"] = "statusIn", ["expectedStatuses"] = new List<object?> { 200L, 201L } } },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Failed, outcome.Status);
        Assert.Equal("Expected status in [200, 201], got 500", outcome.Error);
    }

    [Fact]
    public async Task PathCount_counts_an_arrays_length()
    {
        using var server = new TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await WriteBody(ctx, "{\"items\":[\"a\",\"b\",\"c\"]}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["assert"] = new List<object?> { new Dictionary<string, object?> { ["type"] = "pathCount", ["path"] = "items", ["expectedCount"] = 3L } },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
    }

    [Fact]
    public async Task PathCount_fails_naming_expected_and_actual_when_the_length_differs()
    {
        using var server = new TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await WriteBody(ctx, "{\"items\":[\"a\",\"b\"]}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["assert"] = new List<object?> { new Dictionary<string, object?> { ["type"] = "pathCount", ["path"] = "items", ["expectedCount"] = 5L } },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Failed, outcome.Status);
        Assert.Equal("Path 'items' expected count 5, got 2", outcome.Error);
    }

    [Fact]
    public async Task HeaderEquals_passes_on_an_exact_match_and_fails_naming_actual_on_mismatch()
    {
        using var server = new TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.Headers.Add("X-Trace-Id", "abc-123");
            await WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var passOutcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["assert"] = new List<object?> { new Dictionary<string, object?> { ["type"] = "headerEquals", ["header"] = "X-Trace-Id", ["equals"] = "abc-123" } },
        }, NewContext(), CancellationToken.None);
        Assert.Equal(StepStatus.Passed, passOutcome.Status);

        var failOutcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["assert"] = new List<object?> { new Dictionary<string, object?> { ["type"] = "headerEquals", ["header"] = "X-Trace-Id", ["equals"] = "wrong-value" } },
        }, NewContext(), CancellationToken.None);
        Assert.Equal(StepStatus.Failed, failOutcome.Status);
        Assert.Equal("Header 'X-Trace-Id' expected 'wrong-value', got 'abc-123'", failOutcome.Error);
    }

    [Fact]
    public async Task HeaderContains_matches_a_substring_of_the_headers_value()
    {
        using var server = new TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.Headers.Add("Content-Disposition", "attachment; filename=report.pdf");
            await WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["assert"] = new List<object?> { new Dictionary<string, object?> { ["type"] = "headerContains", ["header"] = "Content-Disposition", ["contains"] = "report.pdf" } },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
    }

    [Fact]
    public async Task ResponseTimeUnder_passes_for_a_generous_budget_and_fails_for_an_impossible_one()
    {
        using var server = new TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await WriteBody(ctx, "{}");
        });

        var module = new ApiModule();
        var passOutcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["assert"] = new List<object?> { new Dictionary<string, object?> { ["type"] = "responseTimeUnder", ["maxMs"] = 60000L } },
        }, NewContext(), CancellationToken.None);
        Assert.Equal(StepStatus.Passed, passOutcome.Status);

        var failOutcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["assert"] = new List<object?> { new Dictionary<string, object?> { ["type"] = "responseTimeUnder", ["maxMs"] = 0L } },
        }, NewContext(), CancellationToken.None);
        Assert.Equal(StepStatus.Failed, failOutcome.Status);
    }

    [Fact]
    public async Task BodyMatchesPattern_regex_matches_the_raw_response_text()
    {
        using var server = new TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await WriteBody(ctx, "order-confirmation-99231");
        });

        var module = new ApiModule();
        var passOutcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["assert"] = new List<object?> { new Dictionary<string, object?> { ["type"] = "bodyMatchesPattern", ["pattern"] = "^order-confirmation-\\d+$" } },
        }, NewContext(), CancellationToken.None);
        Assert.Equal(StepStatus.Passed, passOutcome.Status);

        var failOutcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["assert"] = new List<object?> { new Dictionary<string, object?> { ["type"] = "bodyMatchesPattern", ["pattern"] = "^nope$" } },
        }, NewContext(), CancellationToken.None);
        Assert.Equal(StepStatus.Failed, failOutcome.Status);
    }

    [Fact]
    public async Task BodyMatchesPattern_an_invalid_regex_fails_the_step_cleanly_not_an_unhandled_exception()
    {
        using var server = new TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await WriteBody(ctx, "anything");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["assert"] = new List<object?> { new Dictionary<string, object?> { ["type"] = "bodyMatchesPattern", ["pattern"] = "(unclosed" } },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Failed, outcome.Status);
        Assert.Contains("Invalid regular expression pattern", outcome.Error);
    }

    [Fact]
    public async Task Numeric_path_value_does_not_loosely_equal_a_string_even_when_their_text_matches()
    {
        // Real pre-existing bug, found and fixed building API-T7's own explicit rule
        // ("a string '1' does not equal a number 1"): PlainValueEquality.ValuesEqual's
        // ToString() fallback previously made this pass.
        using var server = new TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await WriteBody(ctx, "{\"id\":1}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["assert"] = new List<object?> { new Dictionary<string, object?> { ["type"] = "pathEquals", ["path"] = "id", ["equals"] = "1" } },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Failed, outcome.Status);
        Assert.Contains("expected \"1\", got 1", outcome.Error);
    }

    [Fact]
    public async Task Numeric_path_value_still_equals_across_integer_and_floating_representations()
    {
        using var server = new TestHttpServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await WriteBody(ctx, "{\"id\":1}");
        });

        var module = new ApiModule();
        var outcome = await module.Run("request", new Dictionary<string, object?>
        {
            ["url"] = server.BaseUrl,
            ["assert"] = new List<object?> { new Dictionary<string, object?> { ["type"] = "pathEquals", ["path"] = "id", ["equals"] = 1.0 } },
        }, NewContext(), CancellationToken.None);

        Assert.Equal(StepStatus.Passed, outcome.Status);
    }

    internal static async Task WriteBody(HttpListenerContext ctx, string json)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.OutputStream.Close();
    }

    /// <summary>Small local HTTP server for exercising ApiModule against real requests/responses.</summary>
    internal sealed class TestHttpServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _acceptLoop;

        public string BaseUrl { get; }

        public TestHttpServer(Func<HttpListenerContext, Task> handler)
        {
            var port = GetFreePort();
            BaseUrl = $"http://127.0.0.1:{port}/";
            _listener = new HttpListener();
            _listener.Prefixes.Add(BaseUrl);
            _listener.Start();
            _acceptLoop = Task.Run(async () =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    HttpListenerContext ctx;
                    try { ctx = await _listener.GetContextAsync(); }
                    catch { return; }

                    try { await handler(ctx); }
                    catch
                    {
                        try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { /* already closed */ }
                    }
                }
            });
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _listener.Close();
        }
    }
}
