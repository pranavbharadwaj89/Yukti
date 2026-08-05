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
    IReadOnlySet<string> CookieNames);

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
