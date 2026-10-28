using Yukti.Domain.UiVocabulary;

namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// Web UI Testing Module Spec, WEB-T4: parses a step param's raw value — already normalized to
/// loosely-typed JSON (IReadOnlyDictionary&lt;string,object?&gt;/List/string) by
/// JsonParamNormalizer — into a real Yukti.Domain.UiVocabulary.Locator, for a step authored with
/// an inline locator object instead of a repository reference or a plain selector string.
///
/// Wire shape: { "primary": {"strategy": "Css", "value": "#submit", "name": null},
/// "fallbacks": [ {...}, ... ], "description": "...", "within": { ...same shape... }, "nth": 1 }.
/// Same dict-to-record parsing style as AssertionParamMapper. Throws ArgumentException on a
/// malformed entry, exactly like AssertionParamMapper — WebModule.Run's own catch turns that
/// into a clean StepOutcome.Failed, never an unhandled exception.
/// </summary>
internal static class LocatorParamMapper
{
    public static Locator Parse(object raw)
    {
        if (raw is not IReadOnlyDictionary<string, object?> dict)
            throw new ArgumentException("An inline locator must be an object with at least a 'primary' field.");

        if (dict.GetValueOrDefault("primary") is not IReadOnlyDictionary<string, object?> primaryRaw)
            throw new ArgumentException("An inline locator requires a 'primary' field.");

        var primary = ParseSpec(primaryRaw);
        var fallbacks = dict.GetValueOrDefault("fallbacks") is IEnumerable<object?> rawFallbacks
            ? rawFallbacks.Select(f => f is IReadOnlyDictionary<string, object?> fd
                ? ParseSpec(fd)
                : throw new ArgumentException("Each 'fallbacks' entry must be an object."))
                .ToList()
            : new List<LocatorSpec>();

        var description = dict.GetValueOrDefault("description") as string;
        var within = dict.GetValueOrDefault("within") is IReadOnlyDictionary<string, object?> withinRaw ? Parse(withinRaw) : null;
        var nth = dict.GetValueOrDefault("nth") is object nthRaw ? Convert.ToInt32(nthRaw) : (int?)null;

        return new Locator(primary, fallbacks, description, within, nth);
    }

    private static LocatorSpec ParseSpec(IReadOnlyDictionary<string, object?> raw)
    {
        var strategyRaw = raw.GetValueOrDefault("strategy") as string
            ?? throw new ArgumentException("A locator spec requires a 'strategy' field.");
        if (!Enum.TryParse<LocatorStrategy>(strategyRaw, ignoreCase: true, out var strategy))
            throw new ArgumentException($"'{strategyRaw}' is not a recognized locator strategy. Supported: " +
                string.Join(", ", Enum.GetNames<LocatorStrategy>()) + ".");

        var value = raw.GetValueOrDefault("value") as string
            ?? throw new ArgumentException("A locator spec requires a 'value' field.");
        var name = raw.GetValueOrDefault("name") as string;

        return new LocatorSpec(strategy, value, name);
    }
}
