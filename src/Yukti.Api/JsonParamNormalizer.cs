using System.Text.Json;

namespace Yukti.Api;

/// <summary>
/// System.Text.Json deserializes a JSON object's dynamic values into
/// Dictionary&lt;string, object?&gt; as boxed JsonElement instances, not
/// plain CLR primitives — every module (ApiModule.Run's `as string` casts,
/// VariableStore's `case string s` pattern match) expects real strings,
/// doubles, bools, lists, and nested dictionaries, exactly like the values
/// that flow through Yukti.Host's hand-written C# dictionaries. Without
/// this normalization step, every step param posted through the HTTP API
/// silently fails its type casts (found running the "add step" -> "trigger
/// run" path end to end: an api.request step with a real "url" string
/// param failed with "requires a 'url' parameter" because `as string`
/// against a JsonElement returns null).
/// </summary>
public static class JsonParamNormalizer
{
    public static Dictionary<string, object?> Normalize(IReadOnlyDictionary<string, object?>? raw)
    {
        var result = new Dictionary<string, object?>();
        if (raw is null) return result;
        foreach (var (key, value) in raw)
            result[key] = NormalizeValue(value);
        return result;
    }

    private static object? NormalizeValue(object? value)
    {
        if (value is not JsonElement element)
            return value;

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => NormalizeValue(p.Value)),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(e => NormalizeValue((object?)e))
                .ToList(),
            _ => value,
        };
    }
}
