namespace Yukti.Domain.Auditing;

/// <summary>
/// D6-T3a: the real, current leak risk — v0.1 has no <c>{{secrets}}</c> syntax (that pipeline is
/// v0.2, Vault-backed, resolved per-step, never stored in the flow), so a user who needs a token
/// in an API step today has exactly one option: paste it directly into a param, most often a
/// header like <c>Authorization</c>. This is the one shared redactor every surface that ever
/// echoes a step's params or a module's output data must call — report writers, audit metadata,
/// anything added later — so there is exactly one place this logic can be wrong, not one per
/// surface.
///
/// Two redaction rules, deliberately different, because a flat param and a header are different
/// shapes:
/// - A top-level param is redacted by its OWN key name (a param literally named "password" or
///   "apiKey" has no legitimate reason to appear in a report).
/// - A nested dictionary value (headers, query params — anything a module's `Run` builds as a
///   flat string-keyed map) is redacted by ITS OWN keys' names, not the outer key ("headers")
///   that contains them — "headers" itself never matches any sensitive fragment, so without this,
///   an entire header map serializes untouched, Authorization value included.
/// </summary>
public static class SecretRedactor
{
    private const string Redacted = "[REDACTED]";

    private static readonly string[] SensitiveParamNameFragments = { "token", "secret", "password", "key", "credential", "auth" };

    private static readonly HashSet<string> SensitiveHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Proxy-Authorization", "Cookie", "Set-Cookie", "X-API-Key", "X-Auth-Token",
    };

    private static readonly string[] SensitiveHeaderNameFragments = { "token", "secret", "key" };

    public static IReadOnlyDictionary<string, object?> Redact(IReadOnlyDictionary<string, object?> values) =>
        values.ToDictionary(kv => kv.Key, kv => RedactValue(kv.Key, kv.Value));

    private static object? RedactValue(string key, object? value)
    {
        if (SensitiveParamNameFragments.Any(f => key.Contains(f, StringComparison.OrdinalIgnoreCase)))
            return Redacted;

        return value switch
        {
            IReadOnlyDictionary<string, object?> nested => RedactHeaderLikeMap(nested),
            _ => value,
        };
    }

    private static IReadOnlyDictionary<string, object?> RedactHeaderLikeMap(IReadOnlyDictionary<string, object?> map) =>
        map.ToDictionary(
            kv => kv.Key,
            kv => IsSensitiveHeaderName(kv.Key) ? Redacted : kv.Value);

    private static bool IsSensitiveHeaderName(string name) =>
        SensitiveHeaderNames.Contains(name) ||
        SensitiveHeaderNameFragments.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase));
}
