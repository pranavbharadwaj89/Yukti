using System.Text.Json;
using Yukti.Domain.Assertions;

namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// Evaluates one Assertion (Yukti.Domain.Assertions) against a response's
/// status code and parsed JSON body. Never throws for a failed assertion —
/// returns (false, message) so ApiModule can collect every failure across
/// the whole assert array instead of failing fast on the first one, per the
/// non-fail-fast behavior documented in docs/specs/modules/api.md.
/// </summary>
internal static class AssertionEvaluator
{
    public static (bool Passed, string? Error) Evaluate(Assertion assertion, int statusCode, JsonElement? body)
    {
        switch (assertion)
        {
            case StatusAssertion a:
                return a.ExpectedStatus == statusCode
                    ? (true, null)
                    : (false, $"Expected status {a.ExpectedStatus}, got {statusCode}");

            case PathExistsAssertion a:
                if (body is null)
                    return (false, $"Path '{a.Path}' does not exist: response body is not JSON");
                return JsonPathEvaluator.TryGetByPath(body.Value, a.Path, out _)
                    ? (true, null)
                    : (false, $"Path '{a.Path}' does not exist");

            case PathEqualsAssertion a:
            {
                if (body is null)
                    return (false, $"Path '{a.Path}' equals check failed: response body is not JSON");
                if (!JsonPathEvaluator.TryGetByPath(body.Value, a.Path, out var element))
                    return (false, $"Path '{a.Path}' does not exist");
                var actual = JsonPathEvaluator.ToPlainValue(element);
                return ValuesEqual(actual, a.ExpectedValue)
                    ? (true, null)
                    : (false, $"Path '{a.Path}' expected {Describe(a.ExpectedValue)}, got {Describe(actual)}");
            }

            case PathContainsAssertion a:
            {
                if (body is null)
                    return (false, $"Path '{a.Path}' contains check failed: response body is not JSON");
                if (!JsonPathEvaluator.TryGetByPath(body.Value, a.Path, out var element))
                    return (false, $"Path '{a.Path}' does not exist");
                var actual = JsonPathEvaluator.ToPlainValue(element);
                return ValueContains(actual, a.ExpectedFragment)
                    ? (true, null)
                    : (false, $"Path '{a.Path}' value {Describe(actual)} does not contain {Describe(a.ExpectedFragment)}");
            }

            default:
                return (false, $"Unsupported assertion type '{assertion.GetType().Name}'");
        }
    }

    private static bool ValuesEqual(object? actual, object? expected)
    {
        if (actual is null || expected is null)
            return actual is null && expected is null;
        if (IsNumeric(actual) && IsNumeric(expected))
            return Convert.ToDouble(actual) == Convert.ToDouble(expected);
        return actual.Equals(expected) || actual.ToString() == expected.ToString();
    }

    private static bool ValueContains(object? actual, object expected)
    {
        switch (actual)
        {
            case string s:
                return s.Contains(expected.ToString() ?? "", StringComparison.Ordinal);
            case System.Collections.IEnumerable list and not string:
                foreach (var item in list)
                    if (ValuesEqual(item, expected))
                        return true;
                return false;
            default:
                return false;
        }
    }

    private static bool IsNumeric(object value) => value is long or int or double or float or decimal;

    private static string Describe(object? value) => value is null ? "null" : JsonSerializer.Serialize(value);
}
