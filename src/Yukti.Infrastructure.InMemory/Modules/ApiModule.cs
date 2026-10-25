using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Yukti.Application.Security;
using Yukti.Contracts;
using Yukti.Domain.Assertions;
using Yukti.Domain.Execution;
using Yukti.Domain.ModulePlugin;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// Real, working API automation — fires genuine HTTP requests (headers,
/// body, query params, timeout), runs the full Assertion hierarchy
/// (Yukti.Domain.Assertions) against status/JSON body — evaluating every
/// assertion, never fail-fast — and exposes the parsed body plus
/// per-assertion results via StepOutcome.Data so later flow steps can chain
/// off it via {{vars.x.y}} interpolation. Port of the original TS
/// prototype's ApiModule to the formal IAutomationModule contract, now with
/// its `assert` array wired up (Volume 1 Part III §18).
/// </summary>
public sealed class ApiModule : IAutomationModule
{
    private const int DefaultTimeoutMs = 10_000;

    /// <summary>
    /// D6-T5: per-instance HttpClients keyed by instance identity, not an
    /// instance field — FR-PLUGIN-07's own contract test
    /// (ModulePluginContractTests.BuiltIn_modules_hold_zero_mutable_instance_state)
    /// asserts ApiModule has zero reflected instance fields (a static,
    /// never-mutated-after-construction HttpClient was already the accepted
    /// shape before D6-T5). A ConditionalWeakTable, itself a static field,
    /// gives each instance its own guard/HttpClient — required so two
    /// ApiModule instances in the same test process (one Restricted, one
    /// default) never share state — while nothing about this changes after
    /// an instance's construction, so concurrent Run() calls on the same
    /// instance still race on nothing, exactly as the contract requires.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ApiModule, HttpClient> HttpClients = new();

    /// <summary>
    /// Guard defaults to permissive so every pre-existing construction site
    /// (CLI, Yukti.Host, the eval host, every test) keeps working against
    /// localhost/FixtureApp exactly as before. Only Yukti.Api's own DI
    /// container supplies the real, config-driven guard — see
    /// ConfiguredOutboundGuard's own doc comment. The check runs at socket
    /// connect time on the resolved IP, inside ConnectCallback, never on the
    /// URL string — so it holds against redirects (each hop opens a new
    /// connection, re-entering this callback) and DNS rebinding (the
    /// resolved address is checked once and connected to directly, never
    /// re-resolved).
    /// </summary>
    public ApiModule(IOutboundGuard? guard = null)
    {
        var effectiveGuard = guard ?? PermissiveOutboundGuard.Instance;
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (context, ct) =>
            {
                // D6-T5 regression, caught live verifying D6-T7's quickstart doc: a dual-stack
                // host (e.g. "localhost", which resolves ::1 before 127.0.0.1 on most machines)
                // must still connect when only one address family actually has something
                // listening — a plain SocketsHttpHandler with no custom ConnectCallback has
                // always tried multiple addresses for exactly this reason. Trying every allowed
                // candidate in resolution order, falling through to the next on a connection
                // failure, restores that — the guard decides which addresses are *allowed*;
                // this loop decides which one actually *works*.
                var addresses = await effectiveGuard.ResolveAllowedAddressesAsync(context.DnsEndPoint.Host, tenantId: null, ct);
                Exception? lastFailure = null;
                foreach (var address in addresses)
                {
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (Exception ex)
                    {
                        socket.Dispose();
                        lastFailure = ex;
                    }
                }
                throw lastFailure!;
            }
        };
        HttpClients.Add(this, new HttpClient(handler));
    }

    private HttpClient Http => HttpClients.TryGetValue(this, out var client)
        ? client
        : throw new InvalidOperationException("ApiModule's HttpClient was never registered — construct it through ApiModule's own constructor, never via Activator/deserialization.");

    public ModuleKind Kind => ModuleKind.Api;
    public string ContractVersion => "1.0.0";

    public IReadOnlyList<ActionSchema> GetSupportedActions() => new[]
    {
        new ActionSchema
        {
            ActionName = "request",
            Description = "Fires an HTTP request and asserts on status/body.",
            Parameters = new[]
            {
                new ParamSpec { Name = "url", Type = ParamType.String, Required = true, Description = "Target URL." },
                new ParamSpec { Name = "method", Type = ParamType.String, Required = false, DefaultValue = "GET", Description = "HTTP method." },
                new ParamSpec { Name = "headers", Type = ParamType.Object, Required = false, Description = "Request headers as a flat string->string object." },
                new ParamSpec { Name = "queryParams", Type = ParamType.Object, Required = false, Description = "Query params appended to the URL as a flat string->string object (last value wins on duplicate keys)." },
                new ParamSpec { Name = "body", Type = ParamType.Object, Required = false, Description = "Request body — a JSON object/array is sent as application/json, a plain string is sent as text/plain (an explicit Content-Type header always wins)." },
                new ParamSpec { Name = "timeoutMs", Type = ParamType.Number, Required = false, DefaultValue = DefaultTimeoutMs, Description = "Request timeout in milliseconds." },
                new ParamSpec { Name = "assert", Type = ParamType.Array, Required = false, Description = "Array of assertions: {type:'status',expectedStatus}, {type:'pathEquals',path,equals}, {type:'pathContains',path,contains}, {type:'pathExists',path}, {type:'headerExists',header}, {type:'cookieExists',cookie}, {type:'schema',schema}. All are evaluated; failures are collected, not fail-fast." },
                new ParamSpec { Name = "expectedStatus", Type = ParamType.Number, Required = false, Description = "Convenience shorthand for a single {type:'status'} assertion; kept for backward compatibility." },
            }
        }
    };

    public Task Setup(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;
    public Task Teardown(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;

    public async Task<StepOutcome> Run(string action, IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct)
    {
        if (action != "request")
            return StepOutcome.Failed($"Unknown api action '{action}'. Supported: 'request'.");

        try
        {
            var url = parameters.GetValueOrDefault("url") as string
                ?? throw new ArgumentException("api.request requires a 'url' parameter.");
            var method = (parameters.GetValueOrDefault("method") as string ?? "GET").ToUpperInvariant();
            var finalUrl = ApplyQueryParams(url, AsStringDictionary(parameters.GetValueOrDefault("queryParams")));
            var assertions = BuildAssertions(parameters);
            var timeoutMs = parameters.TryGetValue("timeoutMs", out var timeoutRaw) && timeoutRaw is not null
                ? Convert.ToInt32(timeoutRaw)
                : DefaultTimeoutMs;

            using var request = new HttpRequestMessage(new HttpMethod(method), finalUrl);
            ApplyBody(request, parameters.GetValueOrDefault("body"));
            ApplyHeaders(request, AsStringDictionary(parameters.GetValueOrDefault("headers")));

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeoutMs);

            var startedAt = DateTimeOffset.UtcNow;
            HttpResponseMessage response;
            string text;
            try
            {
                response = await Http.SendAsync(request, timeoutCts.Token);
                text = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return StepOutcome.Failed($"Request timed out after {timeoutMs}ms");
            }

            using var responseDisposer = response;
            var durationMs = (int)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
            var statusCode = (int)response.StatusCode;

            JsonElement? bodyElement = null;
            object? bodyForOutput = text;
            try
            {
                using var doc = JsonDocument.Parse(text);
                bodyElement = doc.RootElement.Clone();
                bodyForOutput = JsonPathEvaluator.ToPlainValue(bodyElement.Value);
            }
            catch (JsonException) { /* not JSON, keep raw text */ }

            var responseHeaders = response.Headers
                .Concat(response.Content.Headers)
                .ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase);

            var cookieNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookieValues))
            {
                foreach (var rawCookie in setCookieValues)
                {
                    var eq = rawCookie.IndexOf('=');
                    if (eq > 0)
                        cookieNames.Add(rawCookie[..eq].Trim());
                }
            }

            var assertionContext = new AssertionContext(statusCode, bodyElement, responseHeaders, cookieNames);
            var assertionResults = assertions
                .Select(a =>
                {
                    var (passed, error) = AssertionEvaluator.Evaluate(a, assertionContext);
                    return new Dictionary<string, object?> { ["description"] = Describe(a), ["passed"] = passed, ["error"] = error };
                })
                .ToList();

            // A Dictionary<string, object?>, not an anonymous object —
            // VariableStore/ResolvePath only ever walks through
            // IReadOnlyDictionary<string, object?> (same reason
            // JsonPathEvaluator.ToPlainValue already converts the parsed
            // body this way, below). An anonymous type here meant
            // {{vars.<step>.body.id}}-style chaining silently failed to
            // resolve past the first hop for every API step in the whole
            // system, not something specific to one flow — found while
            // building D2-T6's own demo chain, fixed at the source rather
            // than worked around in the flow file.
            var data = new Dictionary<string, object?>
            {
                ["status"] = statusCode,
                ["headers"] = responseHeaders,
                ["body"] = bodyForOutput,
                ["durationMs"] = durationMs,
                ["assertionResults"] = assertionResults,
            };

            var failures = assertionResults.Where(r => r["passed"] is false).Select(r => r["error"] as string).ToList();
            if (failures.Count > 0)
                return StepOutcome.Failed(string.Join("; ", failures), data);

            return StepOutcome.Passed($"{method} {finalUrl} -> {statusCode}", data);
        }
        catch (Exception ex)
        {
            return StepOutcome.Failed(ex.Message);
        }
    }

    private static List<Assertion> BuildAssertions(IReadOnlyDictionary<string, object?> parameters)
    {
        var assertions = new List<Assertion>();

        if (parameters.TryGetValue("expectedStatus", out var expectedRaw) && expectedRaw is not null)
            assertions.Add(new StatusAssertion(Convert.ToInt32(expectedRaw)));

        if (parameters.GetValueOrDefault("assert") is IEnumerable<object?> rawAssertions)
        {
            foreach (var raw in rawAssertions)
            {
                if (raw is not IReadOnlyDictionary<string, object?> dict)
                    throw new ArgumentException("Each 'assert' entry must be an object.");
                assertions.Add(AssertionParamMapper.Parse(dict));
            }
        }

        return assertions;
    }

    private static string Describe(Assertion assertion) => assertion switch
    {
        StatusAssertion a => $"status == {a.ExpectedStatus}",
        PathEqualsAssertion a => $"{a.Path} equals {Json(a.ExpectedValue)}",
        PathContainsAssertion a => $"{a.Path} contains {Json(a.ExpectedFragment)}",
        PathExistsAssertion a => $"{a.Path} exists",
        HeaderExistsAssertion a => $"header '{a.HeaderName}' exists",
        CookieExistsAssertion a => $"cookie '{a.CookieName}' exists",
        SchemaValidationAssertion => "body matches schema",
        _ => assertion.GetType().Name,
    };

    private static string Json(object? value) => value is null ? "null" : JsonSerializer.Serialize(value);

    private static IReadOnlyDictionary<string, string> AsStringDictionary(object? value)
    {
        if (value is not IReadOnlyDictionary<string, object?> dict)
            return new Dictionary<string, string>();
        return dict.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "");
    }

    private static string ApplyQueryParams(string url, IReadOnlyDictionary<string, string> queryParams)
    {
        if (queryParams.Count == 0)
            return url;

        var uri = new Uri(url, UriKind.Absolute);
        var merged = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(uri.Query))
        {
            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = pair.IndexOf('=');
                var key = idx >= 0 ? Uri.UnescapeDataString(pair[..idx]) : Uri.UnescapeDataString(pair);
                var value = idx >= 0 ? Uri.UnescapeDataString(pair[(idx + 1)..]) : "";
                merged[key] = value;
            }
        }
        foreach (var (key, value) in queryParams)
            merged[key] = value; // last-value-wins on duplicate keys

        var query = string.Join("&", merged.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return new UriBuilder(uri) { Query = query }.Uri.ToString();
    }

    private static void ApplyBody(HttpRequestMessage request, object? body)
    {
        switch (body)
        {
            case null:
                return;
            case string s:
                request.Content = new StringContent(s, Encoding.UTF8, "text/plain");
                return;
            default:
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                return;
        }
    }

    private static void ApplyHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string> headers)
    {
        foreach (var (key, value) in headers)
        {
            if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase) && request.Content is not null)
            {
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(value);
                continue;
            }

            if (!request.Headers.TryAddWithoutValidation(key, value) && request.Content is not null)
                request.Content.Headers.TryAddWithoutValidation(key, value);
        }
    }
}
