using System.Text.Json.Nodes;

namespace Yukti.Infrastructure.Ai;

/// <summary>Shared JSON-parsing helpers for Sarvagna capabilities (Analyst, Coder) validating a model's structured output — small and local rather than a generic JSON-Schema library (D5-T3's own stance).</summary>
internal static class JsonNodeHelpers
{
    /// <summary>Null if the property is missing, isn't an array, or any element isn't a plain string — callers treat that as a validation failure, not a partially-read list.</summary>
    public static List<string>? ReadStringArray(JsonNode node, string property)
    {
        if (node[property] is not JsonArray array)
        {
            return null;
        }

        var result = new List<string>();
        foreach (var item in array)
        {
            if (item is not JsonValue value || !value.TryGetValue(out string? s) || s is null)
            {
                return null;
            }
            result.Add(s);
        }
        return result;
    }

    /// <summary>Converts a parsed <see cref="JsonNode"/> into the plain CLR shape (string/bool/double/Dictionary/List)
    /// <c>FlowStep.Params</c>/variable interpolation already expects — mirrors
    /// <c>Yukti.Infrastructure.InMemory.Modules.JsonPathEvaluator.ToPlainValue</c> (which operates on
    /// <see cref="System.Text.Json.JsonElement"/>, a different type this layer has no reason to depend on).</summary>
    public static object? ToPlainValue(JsonNode? node) => node switch
    {
        null => null,
        JsonObject obj => obj.ToDictionary(p => p.Key, p => ToPlainValue(p.Value)),
        JsonArray arr => arr.Select(ToPlainValue).ToList(),
        JsonValue val when val.TryGetValue(out string? s) => s,
        JsonValue val when val.TryGetValue(out bool b) => b,
        JsonValue val when val.TryGetValue(out double d) => d,
        _ => node.ToString(),
    };
}
