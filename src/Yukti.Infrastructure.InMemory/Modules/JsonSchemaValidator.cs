using System.Text.Json;
using Json.Schema;

namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// API-T8 (ADR-0007): the `schema` assertion's real implementation, backed by JsonSchema.Net
/// (draft 2020-12) rather than MinimalJsonSchemaValidator's hand-rolled subset — every violation
/// is collected with its own instance path, not just the first, joined with "; " into the single
/// error string AssertionEvaluator's contract already expects (the same multi-failure join
/// convention used everywhere else in this module). MinimalJsonSchemaValidator itself is untouched
/// — nothing else in this codebase uses it, and it isn't in the way.
/// </summary>
internal static class JsonSchemaValidator
{
    public static (bool Valid, string? Error) Validate(JsonElement instance, object? schema)
    {
        JsonSchema parsedSchema;
        try
        {
            parsedSchema = JsonSerializer.Deserialize<JsonSchema>(JsonSerializer.Serialize(schema))
                ?? throw new InvalidOperationException("Schema deserialized to null.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
        {
            return (false, $"Invalid JSON Schema: {ex.Message}");
        }

        var results = parsedSchema.Evaluate(instance, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (results.IsValid)
            return (true, null);

        var violations = CollectViolations(results).ToList();
        return (false, violations.Count > 0 ? string.Join("; ", violations) : "schema validation failed");
    }

    private static IEnumerable<string> CollectViolations(EvaluationResults results)
    {
        foreach (var detail in results.Details ?? [])
        {
            if (detail.IsValid || detail.Errors is null || detail.Errors.Count == 0)
                continue;

            var location = detail.InstanceLocation.ToString();
            var path = string.IsNullOrEmpty(location) ? "$" : location;
            foreach (var message in detail.Errors.Values)
                yield return $"{path}: {message}";
        }
    }
}
