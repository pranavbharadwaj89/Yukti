using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Yukti.Application.Security;
using Yukti.Contracts;
using Yukti.Domain.Assertions;
using Yukti.Domain.Execution;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
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
    /// API-T3: one CookieContainer per (module instance, FlowRunId) — same
    /// static-ConditionalWeakTable shape as HttpClients above, for the same
    /// reason (FR-PLUGIN-07's zero-reflected-instance-fields contract test
    /// names ApiModule specifically). Created in Setup, removed in Teardown,
    /// exactly WebModule's own ConcurrentDictionary&lt;FlowRunId, WebSession&gt;
    /// precedent — the inner dictionary is what actually gives per-run
    /// isolation; nothing here is shared instance state a concurrent Run()
    /// could race on.
    ///
    /// Real pre-existing bug this fixes as a side effect: SocketsHttpHandler's
    /// UseCookies defaults to true, and since no CookieContainer was ever
    /// explicitly assigned, the handler silently created and reused ONE
    /// implicit container for every request the shared static HttpClient
    /// ever sent — meaning cookies from one flow run could already leak into
    /// a completely unrelated run hitting the same host. UseCookies is now
    /// false below; every cookie is instead attached/captured here, per run.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ApiModule, ConcurrentDictionary<FlowRunId, CookieContainer>> CookieJars = new();

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
                    // Real bug this exact PR's own CI surfaced: constructing the Socket used to sit
                    // outside this try, so a candidate whose address family isn't supported at all on
                    // the host (e.g. InterNetworkV6 with IPv6 disabled at the kernel level, as on some
                    // CI runners) threw out of the whole callback on socket construction, before ever
                    // reaching ConnectAsync — never giving the next candidate a chance. Both failure
                    // modes (can't even open a socket of this family, or opened it but refused) are
                    // exactly the same "this candidate doesn't work, try the next one" case.
                    try
                    {
                        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                        try
                        {
                            await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct);
                            return new NetworkStream(socket, ownsSocket: true);
                        }
                        catch
                        {
                            socket.Dispose();
                            throw;
                        }
                    }
                    catch (Exception ex)
                    {
                        lastFailure = ex;
                    }
                }
                throw lastFailure!;
            }
        };
        // API-T3: see CookieJars' own doc comment — cookies are handled entirely through the
        // per-run jar below, never through SocketsHttpHandler's own (handler-wide, cross-run) one.
        handler.UseCookies = false;
        // API-T3: redirects are followed manually in Run() (see SendOnceAsync's own doc comment)
        // so a step's redirectPolicy can actually be honored, and so the loop controls which hop
        // to re-attach cookies/headers to — the built-in auto-redirect would short-circuit all of
        // that invisibly before Run() ever saw an intermediate response.
        handler.AllowAutoRedirect = false;
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
                new ParamSpec { Name = "pathParams", Type = ParamType.Object, Required = false, Description = "Values for {placeholder} segments in the URL, e.g. {orderId} in '/orders/{orderId}'. Every placeholder needs a matching entry and every entry needs a matching placeholder, or the step fails before the request is sent, naming the mismatch. Values are URL-encoded so they can never add a path segment or query string of their own." },
                new ParamSpec { Name = "queryParams", Type = ParamType.Object, Required = false, Description = "Query params appended to the URL. A scalar value ('page': '2') sends one pair; an array value ('tag': ['a','b']) sends the key repeated, in order. Replaces any value(s) the URL already has for that key." },
                new ParamSpec { Name = "body", Type = ParamType.Object, Required = false, Description = "Request body (see docs/specs/modules/api.md). Default: object/array -> application/json, string -> text/plain. {parts:[...]} -> multipart/form-data (text or file parts; a file part names a file in the configured API files area, never an arbitrary path). A flat object + a 'Content-Type: application/x-www-form-urlencoded' header -> form-encoded." },
                new ParamSpec { Name = "cookies", Type = ParamType.Object, Required = false, Description = "Cookies to send (name->value). Merged into the flow run's own cookie jar, so later steps and this response's Set-Cookie headers compose automatically; the jar never crosses flow runs." },
                new ParamSpec { Name = "redirectPolicy", Type = ParamType.String, Required = false, DefaultValue = "follow", Description = "follow (default) | none | sameHost. Every redirect hop is still sent through the SSRF guard like any other request." },
                new ParamSpec { Name = "timeoutMs", Type = ParamType.Number, Required = false, DefaultValue = DefaultTimeoutMs, Description = "Request timeout in milliseconds, for the whole request including any redirects it follows." },
                new ParamSpec { Name = "assert", Type = ParamType.Array, Required = false, Description = "Array of assertions (see docs/specs/modules/api.md for exact shapes): status, statusIn, pathEquals, pathContains, pathExists, pathCount, headerExists, headerEquals, headerContains, cookieExists, schema, responseTimeUnder, bodyMatchesPattern. All evaluated; failures collected, not fail-fast." },
                new ParamSpec { Name = "expectedStatus", Type = ParamType.Number, Required = false, Description = "Convenience shorthand for a single {type:'status'} assertion; kept for backward compatibility." },
            }
        }
    };

    public Task Setup(ExecutionContext ctx, CancellationToken ct)
    {
        CookieJars.GetOrCreateValue(this)[ctx.RunId] = new CookieContainer();
        return Task.CompletedTask;
    }

    public Task Teardown(ExecutionContext ctx, CancellationToken ct)
    {
        if (CookieJars.TryGetValue(this, out var jars))
            jars.TryRemove(ctx.RunId, out _);
        return Task.CompletedTask;
    }

    /// <summary>Falls back to a fresh, empty jar if Setup was never called (every test that
    /// constructs ApiModule and calls Run directly, without going through FlowEngine) — never
    /// throws, since a step with no prior cookie state is simply a step with no cookies yet.</summary>
    private CookieContainer GetCookieJar(FlowRunId runId) =>
        CookieJars.GetOrCreateValue(this).GetOrAdd(runId, static _ => new CookieContainer());

    public async Task<StepOutcome> Run(string action, IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct)
    {
        if (action != "request")
            return StepOutcome.Failed($"Unknown api action '{action}'. Supported: 'request'.");

        try
        {
            var url = parameters.GetValueOrDefault("url") as string
                ?? throw new ArgumentException("api.request requires a 'url' parameter.");
            var method = (parameters.GetValueOrDefault("method") as string ?? "GET").ToUpperInvariant();
            var urlWithPathParams = ResolvePathParams(url, AsParamDictionary(parameters.GetValueOrDefault("pathParams")));
            var finalUrl = ApplyQueryParams(urlWithPathParams, AsParamDictionary(parameters.GetValueOrDefault("queryParams")));
            var assertions = BuildAssertions(parameters);
            var timeoutMs = parameters.TryGetValue("timeoutMs", out var timeoutRaw) && timeoutRaw is not null
                ? Convert.ToInt32(timeoutRaw)
                : DefaultTimeoutMs;
            var redirectPolicy = (parameters.GetValueOrDefault("redirectPolicy") as string ?? "follow").ToLowerInvariant();
            var headersDict = AsStringDictionary(parameters.GetValueOrDefault("headers"));
            var bodyParam = parameters.GetValueOrDefault("body");
            var jar = GetCookieJar(ctx.RunId);
            ApplyCookies(jar, new Uri(finalUrl, UriKind.Absolute), AsStringDictionary(parameters.GetValueOrDefault("cookies")));

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeoutMs);

            // API-T3: rebuilds a fresh HttpRequestMessage per hop from the step's own
            // declarative inputs (never by trying to resend an already-consumed HttpContent),
            // so a 307/308 can safely carry the exact original body across a redirect. Every
            // hop re-enters ApiModule's own ConnectCallback (a new connection, never reused
            // across hosts), so the SSRF guard applies to every hop, not just the first.
            async Task<(HttpResponseMessage Response, string Text)> SendOnceAsync(string hopMethod, Uri hopUri, bool includeBody)
            {
                using var hopRequest = new HttpRequestMessage(new HttpMethod(hopMethod), hopUri);
                if (includeBody)
                    ApplyBody(hopRequest, bodyParam, headersDict);
                ApplyHeaders(hopRequest, headersDict);

                var cookieHeader = jar.GetCookieHeader(hopUri);
                if (!string.IsNullOrEmpty(cookieHeader))
                    hopRequest.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

                var hopResponse = await Http.SendAsync(hopRequest, timeoutCts.Token);
                var hopText = await hopResponse.Content.ReadAsStringAsync(timeoutCts.Token);
                if (hopResponse.Headers.TryGetValues("Set-Cookie", out var hopSetCookies))
                {
                    foreach (var raw in hopSetCookies)
                    {
                        try { jar.SetCookies(hopUri, raw); }
                        catch (CookieException) { /* malformed Set-Cookie from the server; never fails the step over it */ }
                    }
                }
                return (hopResponse, hopText);
            }

            const int MaxRedirects = 10;
            var currentUri = new Uri(finalUrl, UriKind.Absolute);
            var currentMethod = method;
            var carryBody = true;
            var redirectCount = 0;
            HttpResponseMessage response;
            string text;
            var startedAt = DateTimeOffset.UtcNow;
            try
            {
                while (true)
                {
                    (response, text) = await SendOnceAsync(currentMethod, currentUri, carryBody);
                    var hopStatus = (int)response.StatusCode;
                    var isRedirect = hopStatus is 301 or 302 or 303 or 307 or 308;

                    if (!isRedirect || redirectPolicy == "none" || response.Headers.Location is null)
                        break;

                    var nextUri = response.Headers.Location.IsAbsoluteUri
                        ? response.Headers.Location
                        : new Uri(currentUri, response.Headers.Location);

                    if (redirectPolicy == "samehost" && !string.Equals(nextUri.Host, currentUri.Host, StringComparison.OrdinalIgnoreCase))
                        break;

                    if (++redirectCount > MaxRedirects)
                        throw new InvalidOperationException($"api.request exceeded {MaxRedirects} redirects following {finalUrl}.");

                    // 303 always drops to GET with no body; 301/302 do the same only when the
                    // original request wasn't already GET/HEAD (the long-standing browser/curl
                    // default, since most servers' 301/302 targets assume that); 307/308 are
                    // the explicit "repeat exactly as-is" statuses and always carry method+body.
                    if (hopStatus == 303 || (hopStatus is 301 or 302 && currentMethod is not "GET" and not "HEAD"))
                    {
                        currentMethod = "GET";
                        carryBody = false;
                    }

                    response.Dispose();
                    currentUri = nextUri;
                }
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

            var assertionContext = new AssertionContext(statusCode, bodyElement, responseHeaders, cookieNames, durationMs, text);
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

            return StepOutcome.Passed($"{currentMethod} {currentUri} -> {statusCode}", data);
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
        StatusInAssertion a => $"status in [{string.Join(", ", a.ExpectedStatuses)}]",
        PathCountAssertion a => $"{a.Path} count == {a.ExpectedCount}",
        HeaderEqualsAssertion a => $"header '{a.HeaderName}' equals '{a.ExpectedValue}'",
        HeaderContainsAssertion a => $"header '{a.HeaderName}' contains '{a.Fragment}'",
        ResponseTimeUnderAssertion a => $"response time < {a.Max.TotalMilliseconds}ms",
        BodyMatchesPatternAssertion a => $"body matches /{a.Pattern}/",
        _ => assertion.GetType().Name,
    };

    private static string Json(object? value) => value is null ? "null" : JsonSerializer.Serialize(value);

    private static IReadOnlyDictionary<string, string> AsStringDictionary(object? value)
    {
        if (value is not IReadOnlyDictionary<string, object?> dict)
            return new Dictionary<string, string>();
        return dict.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "");
    }

    // API-T2: pathParams/queryParams need the raw object? values (an array
    // for a repeated query key, a scalar otherwise) — unlike headers, which
    // stay a flat string->string map, so AsStringDictionary's flattening
    // would silently lose that distinction here.
    private static IReadOnlyDictionary<string, object?> AsParamDictionary(object? value) =>
        value as IReadOnlyDictionary<string, object?> ?? new Dictionary<string, object?>();

    private static readonly Regex PathParamPattern = new(@"\{([a-zA-Z0-9_]+)\}", RegexOptions.None);

    /// <summary>
    /// API-T2: resolves {placeholder} segments against `pathParams` before
    /// the query string is touched. Every value goes through
    /// Uri.EscapeDataString, so a value of "../" or "?x=1" is encoded (not
    /// left to become a literal path separator or query delimiter) and can
    /// never add a segment or query string of its own. A template
    /// placeholder with no matching entry, or an entry with no matching
    /// placeholder, fails the step before any request is sent — almost
    /// always a typo, never something to send as a literal "{orderId}".
    /// </summary>
    private static string ResolvePathParams(string urlTemplate, IReadOnlyDictionary<string, object?> pathParams)
    {
        if (pathParams.Count == 0 && !PathParamPattern.IsMatch(urlTemplate))
            return urlTemplate;

        var usedKeys = new HashSet<string>();
        var missingPlaceholders = new List<string>();
        var resolved = PathParamPattern.Replace(urlTemplate, m =>
        {
            var name = m.Groups[1].Value;
            if (!pathParams.TryGetValue(name, out var value) || value is null)
            {
                missingPlaceholders.Add(name);
                return m.Value;
            }
            usedKeys.Add(name);
            return Uri.EscapeDataString(value.ToString() ?? "");
        });

        if (missingPlaceholders.Count > 0)
            throw new ArgumentException(
                $"api.request URL template has placeholder(s) {string.Join(", ", missingPlaceholders.Select(p => $"'{{{p}}}'"))} with no matching 'pathParams' entry.");

        var unmatchedParams = pathParams.Keys.Where(k => !usedKeys.Contains(k)).ToList();
        if (unmatchedParams.Count > 0)
            throw new ArgumentException(
                $"api.request 'pathParams' supplies {string.Join(", ", unmatchedParams.Select(k => $"'{k}'"))} with no matching {{placeholder}} in the URL — likely a typo.");

        return resolved;
    }

    /// <summary>
    /// API-T2: a scalar value replaces every value the URL template's own
    /// query string already has for that key (the pre-API-T2 "last value
    /// wins" behavior); an array value appends one pair per entry, in
    /// order, so repeated keys (?tag=a&amp;tag=b) can be expressed at all.
    /// An empty-string value still appends a pair (?tag=), never dropped.
    /// </summary>
    private static string ApplyQueryParams(string url, IReadOnlyDictionary<string, object?> queryParams)
    {
        if (queryParams.Count == 0)
            return url;

        var uri = new Uri(url, UriKind.Absolute);
        var pairs = new List<KeyValuePair<string, string>>();
        if (!string.IsNullOrEmpty(uri.Query))
        {
            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = pair.IndexOf('=');
                var key = idx >= 0 ? Uri.UnescapeDataString(pair[..idx]) : Uri.UnescapeDataString(pair);
                var value = idx >= 0 ? Uri.UnescapeDataString(pair[(idx + 1)..]) : "";
                pairs.Add(new(key, value));
            }
        }

        foreach (var key in queryParams.Keys)
            pairs.RemoveAll(p => p.Key == key);

        foreach (var (key, raw) in queryParams)
        {
            foreach (var value in ToQueryValues(raw))
                pairs.Add(new(key, value));
        }

        var query = string.Join("&", pairs.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        return new UriBuilder(uri) { Query = query }.Uri.ToString();
    }

    // Matches plain System.Collections.IEnumerable, not IEnumerable<object?> --
    // a generic-interface pattern match doesn't satisfy value-type collections
    // like List<int> or int[] (the same covariance gap AssertionParamMapper's
    // RequireIntList had, API-T7), which would otherwise fall through to the
    // wildcard arm and emit a query value like "System.Int32[]".
    private static IEnumerable<string> ToQueryValues(object? raw) => raw switch
    {
        null => new[] { "" },
        string s => new[] { s },
        System.Collections.IEnumerable list => list.Cast<object?>().Select(v => v?.ToString() ?? ""),
        _ => new[] { raw.ToString() ?? "" },
    };

    /// <summary>
    /// API-T3: a {parts:[...]} shape always means multipart, regardless of any Content-Type
    /// header — the real header (with its boundary) only MultipartFormDataContent can produce
    /// correctly, so a step's own header for it is never trusted. A flat object is form-encoded
    /// only when the step's own header explicitly asks for it: unlike {parts:[...]}, a flat
    /// object's shape alone is genuinely ambiguous with a plain JSON body, so the header is the
    /// only signal that can disambiguate it.
    /// </summary>
    private static void ApplyBody(HttpRequestMessage request, object? body, IReadOnlyDictionary<string, string> headers)
    {
        switch (body)
        {
            case null:
                return;
            case string s:
                request.Content = new StringContent(s, Encoding.UTF8, "text/plain");
                return;
            case IReadOnlyDictionary<string, object?> dict when dict.GetValueOrDefault("parts") is IEnumerable<object?> parts:
                request.Content = BuildMultipartContent(parts);
                return;
            case IReadOnlyDictionary<string, object?> dict when HasContentType(headers, "application/x-www-form-urlencoded"):
                request.Content = new FormUrlEncodedContent(dict.Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value?.ToString() ?? "")));
                return;
            default:
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                return;
        }
    }

    private static bool HasContentType(IReadOnlyDictionary<string, string> headers, string expected)
    {
        foreach (var (key, value) in headers)
        {
            if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                return value.Split(';')[0].Trim().Equals(expected, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    /// <summary>
    /// API-T3: a file part names a bare file in ApiFileStorage's configured area — never an
    /// arbitrary filesystem path. Letting a flow author read any file on the host by path would
    /// be a real file-disclosure hole, not a hypothetical one, so this is deliberately narrower
    /// than the spec's own illustrative "reads from a path the flow supplies" wording.
    /// </summary>
    private static MultipartFormDataContent BuildMultipartContent(IEnumerable<object?> parts)
    {
        var content = new MultipartFormDataContent();
        foreach (var raw in parts)
        {
            if (raw is not IReadOnlyDictionary<string, object?> part)
                throw new ArgumentException("Each 'body.parts' entry must be an object.");

            var name = part.GetValueOrDefault("name") as string
                ?? throw new ArgumentException("Each 'body.parts' entry requires a 'name'.");

            if (part.GetValueOrDefault("fileName") is string requestedFileName)
            {
                var safePath = ApiFileStorage.ResolveSafePath(requestedFileName);
                if (safePath is null || !File.Exists(safePath))
                    throw new ArgumentException($"api.request multipart part '{name}' references file '{requestedFileName}', which was not found.");

                var fileContent = new ByteArrayContent(File.ReadAllBytes(safePath));
                var contentType = part.GetValueOrDefault("contentType") as string ?? "application/octet-stream";
                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
                content.Add(fileContent, name, Path.GetFileName(requestedFileName));
            }
            else
            {
                content.Add(new StringContent(part.GetValueOrDefault("value")?.ToString() ?? ""), name);
            }
        }
        return content;
    }

    private static void ApplyCookies(CookieContainer jar, Uri uri, IReadOnlyDictionary<string, string> cookies)
    {
        foreach (var (name, value) in cookies)
            jar.Add(uri, new Cookie(name, value));
    }

    private static void ApplyHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string> headers)
    {
        foreach (var (key, value) in headers)
        {
            if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase) && request.Content is not null)
            {
                // A multipart body's real Content-Type (with its boundary) comes from
                // MultipartFormDataContent itself — a step's own header for it would be wrong
                // by construction (no boundary), so it's never allowed to overwrite that one.
                if (request.Content is not MultipartFormDataContent)
                    request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(value);
                continue;
            }

            if (!request.Headers.TryAddWithoutValidation(key, value) && request.Content is not null)
                request.Content.Headers.TryAddWithoutValidation(key, value);
        }
    }
}
