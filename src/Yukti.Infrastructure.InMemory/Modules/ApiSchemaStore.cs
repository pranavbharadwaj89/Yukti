using System.Text.Json;

namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// API-T8: the reusable-schema-artifact half of the `schema` assertion — a `schema` value that's
/// a string names a schema stored here, loaded as the plain CLR object graph every other schema
/// value arrives in (so AssertionParamMapper's own type doesn't need to know the difference).
/// Same shape as ApiFileStorage (API-T3): a single configured directory, bare file name only
/// (Path.GetFileName strips any directory component, so traversal can't escape it), not yet
/// tenant- or run-scoped — there is no upload mechanism to hang that scoping from yet. Treat it as
/// shared, operator-managed fixtures, not tenant data, exactly like ApiFileStorage's own gap.
/// </summary>
internal static class ApiSchemaStore
{
    public static string RootDirectory =>
        Environment.GetEnvironmentVariable("YUKTI_API_SCHEMAS_DIR")
        ?? Path.Combine(AppContext.BaseDirectory, "api-schemas");

    public static object LoadByName(string name)
    {
        var fileName = Path.GetFileName(name);
        if (string.IsNullOrEmpty(fileName))
            throw new ArgumentException($"api schema reference '{name}' is not a valid schema name.");

        var path = Path.Combine(RootDirectory, $"{fileName}.json");
        if (!File.Exists(path))
            throw new ArgumentException($"api schema reference '{name}' was not found (looked for '{fileName}.json' in the configured API schemas area).");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return JsonPathEvaluator.ToPlainValue(doc.RootElement)
            ?? throw new ArgumentException($"api schema reference '{name}' resolved to a null document.");
    }
}
