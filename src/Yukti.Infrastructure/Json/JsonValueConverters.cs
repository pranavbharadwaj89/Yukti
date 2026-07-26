using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Yukti.Infrastructure.Json;

/// <summary>
/// FlowStep.Params, FlowRun.Variables, and StepResult.Data are all
/// Dictionary&lt;string, object?&gt; — dynamic, per-module-defined shapes
/// that don't belong as relational columns. Stored as jsonb.
///
/// System.Text.Json deserializes a JSON object's dynamic values into boxed
/// JsonElement instances, not plain CLR primitives — the exact same issue
/// JsonParamNormalizer (Yukti.Api) fixes for HTTP request bodies. Round-
/// tripping through Postgres/CockroachDB jsonb hits the identical problem
/// on read, so this converter normalizes on the way back out too — a
/// module reading `parameters.GetValueOrDefault("url") as string` must see
/// a real string whether the flow was just authored via HTTP or reloaded
/// from the database three days later.
/// </summary>
public static class JsonValueConverters
{
    private static readonly JsonSerializerOptions Options = new();

    public static readonly ValueConverter<IReadOnlyDictionary<string, object?>, string> Dictionary = new(
        dict => JsonSerializer.Serialize(dict, Options),
        json => Normalize(JsonSerializer.Deserialize<Dictionary<string, object?>>(json, Options) ?? new()));

    public static readonly ValueConverter<object?, string> NullableObject = new(
        value => JsonSerializer.Serialize(value, Options),
        json => NormalizeValue(JsonSerializer.Deserialize<object?>(json, Options)));

    public static readonly ValueComparer<IReadOnlyDictionary<string, object?>> DictionaryComparer = new(
        (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
        d => JsonSerializer.Serialize(d, Options).GetHashCode(),
        d => Normalize(JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(d, Options), Options) ?? new()));

    private static Dictionary<string, object?> Normalize(Dictionary<string, object?> raw)
    {
        var result = new Dictionary<string, object?>();
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
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => NormalizeValue(p.Value)),
            JsonValueKind.Array => element.EnumerateArray().Select(e => NormalizeValue((object?)e)).ToList(),
            _ => value,
        };
    }
}
