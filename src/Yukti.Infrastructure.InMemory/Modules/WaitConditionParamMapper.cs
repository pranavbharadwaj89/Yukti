using Yukti.Domain.UiVocabulary;

namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// Web UI Testing Module Spec, WEB-T5: parses a `waitFor` step's "condition" param -- already
/// normalized to loosely-typed JSON by JsonParamNormalizer -- into one of the 13
/// Yukti.Domain.UiVocabulary.WaitCondition records (WEB-T1). Same dict-to-record parsing style
/// as AssertionParamMapper/LocatorParamMapper. A condition's own "locator" field, when present,
/// arrives either as an inline locator dict (parsed here via LocatorParamMapper) or as an
/// already-resolved Locator (FlowEngine.ResolveLocatorRef resolved a nested RepositoryRef before
/// WebModule ever saw it).
/// </summary>
internal static class WaitConditionParamMapper
{
    public static WaitCondition Parse(object raw)
    {
        if (raw is not IReadOnlyDictionary<string, object?> dict)
            throw new ArgumentException("A 'condition' must be an object with at least a 'type' field.");

        var type = dict.GetValueOrDefault("type") as string
            ?? throw new ArgumentException("A 'condition' requires a 'type' field.");

        return type switch
        {
            "visible" => new Visible(RequireLocator(dict, type)),
            "hidden" => new Hidden(RequireLocator(dict, type)),
            "enabled" => new Enabled(RequireLocator(dict, type)),
            "disabled" => new Disabled(RequireLocator(dict, type)),
            "textEquals" => new TextEquals(RequireLocator(dict, type), RequireString(dict, "expected", type)),
            "textContains" => new TextContains(RequireLocator(dict, type), RequireString(dict, "fragment", type)),
            "attributeEquals" => new AttributeEquals(RequireLocator(dict, type), RequireString(dict, "attr", type), RequireString(dict, "expected", type)),
            "countEquals" => new CountEquals(RequireLocator(dict, type), RequireInt(dict, "expected", type)),
            "urlEquals" => new UrlEquals(RequireString(dict, "expected", type)),
            "urlContains" => new UrlContains(RequireString(dict, "fragment", type)),
            "titleContains" => new TitleContains(RequireString(dict, "fragment", type)),
            "networkIdle" => new NetworkIdle(TimeSpan.FromMilliseconds(dict.GetValueOrDefault("quietMs") is object quietRaw ? Convert.ToInt32(quietRaw) : 500)),
            "customPredicate" => new CustomPredicate(RequireString(dict, "script", type)),
            _ => throw new ArgumentException($"Unknown condition type '{type}'. Supported: visible, hidden, enabled, disabled, " +
                "textEquals, textContains, attributeEquals, countEquals, urlEquals, urlContains, titleContains, networkIdle, customPredicate."),
        };
    }

    private static Locator RequireLocator(IReadOnlyDictionary<string, object?> dict, string type)
    {
        var raw = dict.GetValueOrDefault("locator")
            ?? throw new ArgumentException($"condition type '{type}' requires a 'locator' field.");
        return raw as Locator ?? LocatorParamMapper.Parse(raw);
    }

    private static string RequireString(IReadOnlyDictionary<string, object?> dict, string key, string type) =>
        dict.GetValueOrDefault(key) as string
        ?? throw new ArgumentException($"condition type '{type}' requires a '{key}' string field.");

    private static int RequireInt(IReadOnlyDictionary<string, object?> dict, string key, string type)
    {
        if (!dict.TryGetValue(key, out var value) || value is null)
            throw new ArgumentException($"condition type '{type}' requires a '{key}' field.");
        return Convert.ToInt32(value);
    }
}
