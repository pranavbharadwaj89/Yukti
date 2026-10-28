using System.Text.Json;
using Yukti.Domain.Assertions;

namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// Everything one Assertion needs to see about a response — status, parsed
/// JSON body (null if the body wasn't valid JSON), response headers
/// (case-insensitive), and the set of cookie names set via Set-Cookie.
/// Built once per request in ApiModule.Run and reused across every entry
/// in the assert array.
/// </summary>
internal readonly record struct AssertionContext(
    int StatusCode,
    JsonElement? Body,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlySet<string> CookieNames,
    int DurationMs = 0,
    string? RawBody = null);

/// <summary>
/// Evaluates one Assertion (Yukti.Domain.Assertions) against an
/// AssertionContext. Never throws for a failed assertion — returns (false,
/// message) so ApiModule can collect every failure across the whole assert
/// array instead of failing fast on the first one, per the non-fail-fast
/// behavior documented in docs/specs/modules/api.md.
/// </summary>
internal static class AssertionEvaluator
{
    public static (bool Passed, string? Error) Evaluate(Assertion assertion, AssertionContext context)
    {
        switch (assertion)
        {
            case StatusAssertion a:
                return a.ExpectedStatus == context.StatusCode
                    ? (true, null)
                    : (false, $"Expected status {a.ExpectedStatus}, got {context.StatusCode}");

            case PathExistsAssertion a:
                if (context.Body is null)
                    return (false, $"Path '{a.Path}' does not exist: response body is not JSON");
                return JsonPathEvaluator.TryGetByPath(context.Body.Value, a.Path, out _)
                    ? (true, null)
                    : (false, $"Path '{a.Path}' does not exist");

            case PathEqualsAssertion a:
                {
                    if (context.Body is null)
                        return (false, $"Path '{a.Path}' equals check failed: response body is not JSON");
                    if (!JsonPathEvaluator.TryGetByPath(context.Body.Value, a.Path, out var element))
                        return (false, $"Path '{a.Path}' does not exist");
                    var actual = JsonPathEvaluator.ToPlainValue(element);
                    return PlainValueEquality.ValuesEqual(actual, a.ExpectedValue)
                        ? (true, null)
                        : (false, $"Path '{a.Path}' expected {Describe(a.ExpectedValue)}, got {Describe(actual)}");
                }

            case PathContainsAssertion a:
                {
                    if (context.Body is null)
                        return (false, $"Path '{a.Path}' contains check failed: response body is not JSON");
                    if (!JsonPathEvaluator.TryGetByPath(context.Body.Value, a.Path, out var element))
                        return (false, $"Path '{a.Path}' does not exist");
                    var actual = JsonPathEvaluator.ToPlainValue(element);
                    return ValueContains(actual, a.ExpectedFragment)
                        ? (true, null)
                        : (false, $"Path '{a.Path}' value {Describe(actual)} does not contain {Describe(a.ExpectedFragment)}");
                }

            case HeaderExistsAssertion a:
                return context.Headers.ContainsKey(a.HeaderName)
                    ? (true, null)
                    : (false, $"Header '{a.HeaderName}' does not exist");

            case CookieExistsAssertion a:
                return context.CookieNames.Contains(a.CookieName)
                    ? (true, null)
                    : (false, $"Cookie '{a.CookieName}' does not exist");

            case SchemaValidationAssertion a:
                {
                    if (context.Body is null)
                        return (false, "Schema validation failed: response body is not JSON");
                    var (valid, error) = MinimalJsonSchemaValidator.Validate(context.Body.Value, a.Schema);
                    return valid ? (true, null) : (false, $"Schema validation failed: {error}");
                }

            case StatusInAssertion a:
                return a.ExpectedStatuses.Contains(context.StatusCode)
                    ? (true, null)
                    : (false, $"Expected status in [{string.Join(", ", a.ExpectedStatuses)}], got {context.StatusCode}");

            case PathCountAssertion a:
                {
                    if (context.Body is null)
                        return (false, $"Path '{a.Path}' count check failed: response body is not JSON");
                    if (!JsonPathEvaluator.TryGetByPath(context.Body.Value, a.Path, out var element))
                        return (false, $"Path '{a.Path}' does not exist");
                    var actualCount = element.ValueKind == JsonValueKind.Array ? element.GetArrayLength() : 1;
                    return actualCount == a.ExpectedCount
                        ? (true, null)
                        : (false, $"Path '{a.Path}' expected count {a.ExpectedCount}, got {actualCount}");
                }

            case HeaderEqualsAssertion a:
                if (!context.Headers.TryGetValue(a.HeaderName, out var headerValue))
                    return (false, $"Header '{a.HeaderName}' does not exist");
                return headerValue == a.ExpectedValue
                    ? (true, null)
                    : (false, $"Header '{a.HeaderName}' expected '{a.ExpectedValue}', got '{headerValue}'");

            case HeaderContainsAssertion a:
                if (!context.Headers.TryGetValue(a.HeaderName, out var containsValue))
                    return (false, $"Header '{a.HeaderName}' does not exist");
                return containsValue.Contains(a.Fragment, StringComparison.Ordinal)
                    ? (true, null)
                    : (false, $"Header '{a.HeaderName}' value '{containsValue}' does not contain '{a.Fragment}'");

            case ResponseTimeUnderAssertion a:
                return context.DurationMs < a.Max.TotalMilliseconds
                    ? (true, null)
                    : (false, $"Expected response time under {a.Max.TotalMilliseconds}ms, took {context.DurationMs}ms");

            case BodyMatchesPatternAssertion a:
                {
                    var body = context.RawBody ?? "";
                    return System.Text.RegularExpressions.Regex.IsMatch(body, a.Pattern)
                        ? (true, null)
                        : (false, $"Body does not match pattern '{a.Pattern}'");
                }

            default:
                return (false, $"Unsupported assertion type '{assertion.GetType().Name}'");
        }
    }

    private static bool ValueContains(object? actual, object expected)
    {
        switch (actual)
        {
            case string s:
                return s.Contains(expected.ToString() ?? "", StringComparison.Ordinal);
            case System.Collections.IEnumerable list and not string:
                foreach (var item in list)
                    if (PlainValueEquality.ValuesEqual(item, expected))
                        return true;
                return false;
            default:
                return false;
        }
    }

    private static string Describe(object? value) => value is null ? "null" : JsonSerializer.Serialize(value);
}
