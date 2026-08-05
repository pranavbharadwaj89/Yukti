using System.Text.Json;

namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// A deliberately small subset of JSON Schema — `type`, `required`,
/// `properties`, `items`, `enum` only. No `$ref`, no `oneOf`/`anyOf`/
/// `allOf`/`not`, no `pattern`/`format`/`minimum`/`maximum`/string length
/// bounds, no draft version negotiation. Matches the same "hand-rolled,
/// documented scope-down" convention JsonPathEvaluator already established
/// for this module rather than pulling in a full JSON Schema library — see
/// docs/specs/modules/api.md's "Known constraints" for the exact boundary.
/// The schema itself is a plain CLR object graph (Dictionary&lt;string,
/// object?&gt;/List&lt;object?&gt;/primitives), the same normalized shape
/// every other assert-array param arrives in.
/// </summary>
internal static class MinimalJsonSchemaValidator
{
    public static (bool Valid, string? Error) Validate(JsonElement instance, object? schema) =>
        schema is IReadOnlyDictionary<string, object?> schemaDict
            ? ValidateNode(instance, schemaDict, "$")
            : (false, "Schema must be a JSON object.");

    private static (bool Valid, string? Error) ValidateNode(JsonElement instance, IReadOnlyDictionary<string, object?> schema, string path)
    {
        if (schema.GetValueOrDefault("type") is string expectedType && !MatchesType(instance, expectedType))
            return (false, $"{path}: expected type '{expectedType}', got '{DescribeKind(instance.ValueKind)}'");

        if (schema.GetValueOrDefault("enum") is IEnumerable<object?> allowed)
        {
            var actual = JsonPathEvaluator.ToPlainValue(instance);
            if (!allowed.Any(v => PlainValueEquality.ValuesEqual(actual, v)))
                return (false, $"{path}: value is not one of the allowed enum values");
        }

        if (instance.ValueKind == JsonValueKind.Object)
        {
            if (schema.GetValueOrDefault("required") is IEnumerable<object?> required)
            {
                foreach (var requiredName in required.OfType<string>())
                {
                    if (!instance.TryGetProperty(requiredName, out _))
                        return (false, $"{path}: missing required property '{requiredName}'");
                }
            }

            if (schema.GetValueOrDefault("properties") is IReadOnlyDictionary<string, object?> properties)
            {
                foreach (var (propName, propSchema) in properties)
                {
                    if (!instance.TryGetProperty(propName, out var propValue) || propSchema is not IReadOnlyDictionary<string, object?> propSchemaDict)
                        continue; // a missing optional property, or a non-object subschema, is not this pass's job to flag

                    var result = ValidateNode(propValue, propSchemaDict, $"{path}.{propName}");
                    if (!result.Valid)
                        return result;
                }
            }
        }

        if (instance.ValueKind == JsonValueKind.Array && schema.GetValueOrDefault("items") is IReadOnlyDictionary<string, object?> itemSchema)
        {
            var index = 0;
            foreach (var item in instance.EnumerateArray())
            {
                var result = ValidateNode(item, itemSchema, $"{path}[{index}]");
                if (!result.Valid)
                    return result;
                index++;
            }
        }

        return (true, null);
    }

    private static bool MatchesType(JsonElement instance, string expectedType) => expectedType switch
    {
        "string" => instance.ValueKind == JsonValueKind.String,
        "number" => instance.ValueKind == JsonValueKind.Number,
        "integer" => instance.ValueKind == JsonValueKind.Number && instance.TryGetInt64(out _),
        "boolean" => instance.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "object" => instance.ValueKind == JsonValueKind.Object,
        "array" => instance.ValueKind == JsonValueKind.Array,
        "null" => instance.ValueKind == JsonValueKind.Null,
        _ => true, // an unrecognized declared type is not this subset's job to reject — skip the check rather than false-fail
    };

    private static string DescribeKind(JsonValueKind kind) => kind switch
    {
        JsonValueKind.String => "string",
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Object => "object",
        JsonValueKind.Array => "array",
        JsonValueKind.Null => "null",
        _ => kind.ToString(),
    };
}
