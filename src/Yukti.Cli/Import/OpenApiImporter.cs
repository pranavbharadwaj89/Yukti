using System.Text.Json;

namespace Yukti.Cli.Import;

/// <summary>
/// API Testing Module Spec, API-T10: reads an OpenAPI 3.x document (JSON only — no YAML, no
/// Swagger 2.0; a real, documented scope-down rather than pulling in a new NuGet dependency for
/// a format negotiation this task doesn't require) and generates one `api`/`request` step per
/// selected operation, with path/query/header parameters wired to `{{vars.&lt;name&gt;}}`
/// placeholders and a `schema` assertion pre-filled from the first 2xx response's own JSON Schema.
/// A request body can only ever be described by OpenAPI as a *schema*, never a real value, so it
/// is never fabricated — the operation is flagged in `Warnings` instead, naming the step.
/// </summary>
public static class OpenApiImporter
{
    private static readonly HashSet<string> HttpMethods = new(StringComparer.OrdinalIgnoreCase)
        { "get", "put", "post", "delete", "options", "head", "patch", "trace" };

    public static ImportResult Import(string openApiJson, IReadOnlyCollection<string>? operationIds = null)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(openApiJson);
        }
        catch (JsonException ex)
        {
            return ImportResult.Failed($"Not valid JSON: {ex.Message}");
        }
        using var _ = doc;
        var root = doc.RootElement;

        if (!root.TryGetProperty("openapi", out var versionEl) || versionEl.GetString() is not string version || !version.StartsWith("3."))
            return ImportResult.Failed("Only OpenAPI 3.x JSON documents are supported (no YAML, no Swagger 2.0).");

        if (!root.TryGetProperty("paths", out var pathsEl) || pathsEl.ValueKind != JsonValueKind.Object)
            return ImportResult.Failed("Document has no 'paths' object.");

        var warnings = new List<string>();
        var baseUrl = "";
        if (root.TryGetProperty("servers", out var serversEl) && serversEl.ValueKind == JsonValueKind.Array && serversEl.GetArrayLength() > 0
            && serversEl[0].TryGetProperty("url", out var urlEl) && urlEl.GetString() is string url)
        {
            baseUrl = url;
        }
        else
        {
            warnings.Add("No 'servers[0].url' found — generated URLs are path-only; fill in a base URL manually.");
        }

        var title = root.TryGetProperty("info", out var infoEl) && infoEl.TryGetProperty("title", out var titleEl)
            ? titleEl.GetString() : null;
        var dto = new FlowFileDto { Name = title ?? "Imported OpenAPI flow" };

        foreach (var pathProp in pathsEl.EnumerateObject())
        {
            var pathTemplate = pathProp.Name;
            if (pathProp.Value.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var methodProp in pathProp.Value.EnumerateObject())
            {
                if (!HttpMethods.Contains(methodProp.Name))
                    continue; // siblings like "parameters"/"$ref"/"summary" at the path-item level, not an operation

                var operation = methodProp.Value;
                var operationId = operation.TryGetProperty("operationId", out var opIdEl) ? opIdEl.GetString() : null;
                if (operationIds is { Count: > 0 } && (operationId is null || !operationIds.Contains(operationId)))
                    continue;

                dto.Steps.Add(BuildStep(pathTemplate, methodProp.Name, operation, baseUrl, operationId, warnings));
            }
        }

        if (dto.Steps.Count == 0)
        {
            return ImportResult.Failed(operationIds is { Count: > 0 }
                ? "None of the requested operationIds were found in the document."
                : "The document's 'paths' object has no operations to import.");
        }

        return new ImportResult(dto, warnings);
    }

    private static FlowFileStepDto BuildStep(
        string pathTemplate, string method, JsonElement operation, string baseUrl, string? operationId, List<string> warnings)
    {
        var stepName = operationId ?? $"{method.ToUpperInvariant()} {pathTemplate}";
        var pathParams = new Dictionary<string, object?>();
        var queryParams = new Dictionary<string, object?>();
        var headers = new Dictionary<string, object?>();

        if (operation.TryGetProperty("parameters", out var paramsEl) && paramsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var param in paramsEl.EnumerateArray())
            {
                if (!param.TryGetProperty("name", out var nameEl) || nameEl.GetString() is not string name)
                    continue;
                var location = param.TryGetProperty("in", out var inEl) ? inEl.GetString() : null;
                var placeholder = $"{{{{vars.{name}}}}}";
                switch (location)
                {
                    case "path": pathParams[name] = placeholder; break;
                    case "query": queryParams[name] = placeholder; break;
                    case "header": headers[name] = placeholder; break;
                    default:
                        warnings.Add($"{stepName}: parameter '{name}' (in: '{location}') has no Yukti equivalent, skipped.");
                        break;
                }
            }
        }

        var parameters = new Dictionary<string, object?>
        {
            ["url"] = baseUrl + pathTemplate,
            ["method"] = method.ToUpperInvariant(),
        };
        if (pathParams.Count > 0) parameters["pathParams"] = pathParams;
        if (queryParams.Count > 0) parameters["queryParams"] = queryParams;
        if (headers.Count > 0) parameters["headers"] = headers;

        if (operation.TryGetProperty("requestBody", out _))
            warnings.Add($"{stepName}: has a requestBody — OpenAPI only describes its schema, not a real value, so 'body' was left empty; fill it in manually.");

        var schema = FindFirstSuccessJsonSchema(operation);
        if (schema is not null)
        {
            parameters["assert"] = new List<object?>
            {
                new Dictionary<string, object?> { ["type"] = "schema", ["schema"] = schema },
            };
        }

        return new FlowFileStepDto { Name = stepName, Module = "api", Action = "request", Params = parameters };
    }

    private static object? FindFirstSuccessJsonSchema(JsonElement operation)
    {
        if (!operation.TryGetProperty("responses", out var responses) || responses.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var statusProp in responses.EnumerateObject())
        {
            if (statusProp.Name.Length != 3 || statusProp.Name[0] != '2')
                continue; // only a 2xx response's schema is worth pre-filling as an assertion

            if (statusProp.Value.TryGetProperty("content", out var content)
                && content.TryGetProperty("application/json", out var mediaType)
                && mediaType.TryGetProperty("schema", out var schemaEl))
            {
                return FlowFileJson.Normalize(schemaEl);
            }
        }
        return null;
    }
}
