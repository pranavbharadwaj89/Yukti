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
            _ => throw new ArgumentException($"Unknown assertion type '{type}'. Supported: status, pathEquals, pathContains, pathExists, headerExists, cookieExists, schema."),
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
}
