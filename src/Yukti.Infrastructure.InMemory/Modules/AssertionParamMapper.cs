using Yukti.Domain.Assertions;

namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// Parses one element of the api.request "assert" array param (a
/// type-discriminated JSON object, already normalized to
/// IReadOnlyDictionary&lt;string,object?&gt; by JsonParamNormalizer at the
/// API layer) into the matching Yukti.Domain.Assertions.Assertion record.
/// Throws ArgumentException on an unknown/malformed entry — ApiModule.Run
/// catches this alongside every other param-parsing failure, so a bad
/// assert entry becomes a clean StepOutcome.Failed, not an unhandled
/// exception.
/// </summary>
internal static class AssertionParamMapper
{
    public static Assertion Parse(IReadOnlyDictionary<string, object?> raw)
    {
        var type = raw.GetValueOrDefault("type") as string
            ?? throw new ArgumentException("Each 'assert' entry requires a 'type' field.");

        return type switch
        {
            "status" => new StatusAssertion(RequireInt(raw, "expectedStatus", type)),
            "pathEquals" => new PathEqualsAssertion(RequireString(raw, "path", type), raw.GetValueOrDefault("equals")),
            "pathContains" => new PathContainsAssertion(RequireString(raw, "path", type), RequireAny(raw, "contains", type)),
            "pathExists" => new PathExistsAssertion(RequireString(raw, "path", type)),
            "headerExists" => new HeaderExistsAssertion(RequireString(raw, "header", type)),
            "cookieExists" => new CookieExistsAssertion(RequireString(raw, "cookie", type)),
            "schema" => new SchemaValidationAssertion(RequireAny(raw, "schema", type)),
            "statusIn" => new StatusInAssertion(RequireIntList(raw, "expectedStatuses", type)),
            "pathCount" => new PathCountAssertion(RequireString(raw, "path", type), RequireInt(raw, "expectedCount", type)),
            "headerEquals" => new HeaderEqualsAssertion(RequireString(raw, "header", type), RequireString(raw, "equals", type)),
            "headerContains" => new HeaderContainsAssertion(RequireString(raw, "header", type), RequireString(raw, "contains", type)),
            "responseTimeUnder" => new ResponseTimeUnderAssertion(TimeSpan.FromMilliseconds(RequireInt(raw, "maxMs", type))),
            "bodyMatchesPattern" => new BodyMatchesPatternAssertion(RequireString(raw, "pattern", type)),
            _ => throw new ArgumentException($"Unknown assertion type '{type}'. Supported: status, statusIn, pathEquals, pathContains, pathExists, pathCount, headerExists, headerEquals, headerContains, cookieExists, schema, responseTimeUnder, bodyMatchesPattern."),
        };
    }

    private static string RequireString(IReadOnlyDictionary<string, object?> raw, string key, string type) =>
        raw.GetValueOrDefault(key) as string
        ?? throw new ArgumentException($"assert type '{type}' requires a '{key}' string field.");

    private static int RequireInt(IReadOnlyDictionary<string, object?> raw, string key, string type)
    {
        if (!raw.TryGetValue(key, out var value) || value is null)
            throw new ArgumentException($"assert type '{type}' requires a '{key}' field.");
        return Convert.ToInt32(value);
    }

    private static object RequireAny(IReadOnlyDictionary<string, object?> raw, string key, string type) =>
        raw.GetValueOrDefault(key)
        ?? throw new ArgumentException($"assert type '{type}' requires a '{key}' field.");

    private static IReadOnlyList<int> RequireIntList(IReadOnlyDictionary<string, object?> raw, string key, string type)
    {
        // Plain System.Collections.IEnumerable, not IEnumerable<object?> — a typed numeric
        // collection (e.g. List<long>) doesn't satisfy the generic interface since value types
        // aren't covariant, even though every real caller today arrives as List<object?> via
        // JsonParamNormalizer.
        if (raw.GetValueOrDefault(key) is not System.Collections.IEnumerable items || items is string)
            throw new ArgumentException($"assert type '{type}' requires a '{key}' array field.");
        return items.Cast<object?>().Select(i => Convert.ToInt32(i)).ToList();
    }
}
