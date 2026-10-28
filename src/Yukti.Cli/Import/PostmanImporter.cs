using System.Text.Json;
using System.Text.RegularExpressions;

namespace Yukti.Cli.Import;

/// <summary>
/// API Testing Module Spec, API-T10: converts a Postman Collection v2.1 export into `api`/
/// `request` steps. Postman's own `{{name}}` template syntax maps directly onto Yukti's
/// `{{vars.name}}` — "variables map to Yukti variables," the spec's own checklist item, is this
/// substitution. Folders (`item` nesting) are flattened recursively. Anything with no Yukti
/// equivalent — a dynamic variable (`{{$guid}}`), a non-raw/non-JSON body, an auth scheme beyond
/// basic/bearer, a pre-request/test script — is named in `Warnings`, never silently dropped.
/// </summary>
public static class PostmanImporter
{
    private static readonly Regex VarPattern = new(@"\{\{\s*([^{}]+?)\s*\}\}", RegexOptions.Compiled);
    private static readonly Regex PathParamPattern = new(@":([a-zA-Z0-9_]+)", RegexOptions.Compiled);

    public static ImportResult Import(string collectionJson)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(collectionJson);
        }
        catch (JsonException ex)
        {
            return ImportResult.Failed($"Not valid JSON: {ex.Message}");
        }
        using var _ = doc;
        var root = doc.RootElement;

        if (!root.TryGetProperty("info", out var infoEl) || !infoEl.TryGetProperty("schema", out var schemaEl)
            || schemaEl.GetString() is not string schemaUrl || !schemaUrl.Contains("v2.1"))
        {
            return ImportResult.Failed("Only Postman Collection v2.1 ('info.schema' containing \"v2.1\") is supported.");
        }

        var name = infoEl.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
        var dto = new FlowFileDto { Name = name ?? "Imported Postman flow" };
        var warnings = new List<string>();
        var declaredVariables = new HashSet<string>();

        if (root.TryGetProperty("variable", out var collectionVarsEl) && collectionVarsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in collectionVarsEl.EnumerateArray())
            {
                if (v.TryGetProperty("key", out var keyEl) && keyEl.GetString() is string key)
                    declaredVariables.Add(key);
            }
        }

        if (root.TryGetProperty("item", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
            CollectItems(itemsEl, "", dto, warnings, declaredVariables);

        if (dto.Steps.Count == 0)
            return ImportResult.Failed("The collection has no requests to import.");

        dto.DeclaredVariables.AddRange(declaredVariables.Order(StringComparer.Ordinal));
        return new ImportResult(dto, warnings);
    }

    private static void CollectItems(JsonElement items, string folderPrefix, FlowFileDto dto, List<string> warnings, HashSet<string> declaredVariables)
    {
        foreach (var item in items.EnumerateArray())
        {
            var itemName = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "Unnamed" : "Unnamed";

            if (item.TryGetProperty("item", out var nestedEl) && nestedEl.ValueKind == JsonValueKind.Array)
            {
                // A Postman "folder" — same shape as the collection root, recursed with its name
                // prefixed so flattened step names stay unique and traceable to their folder.
                CollectItems(nestedEl, folderPrefix + itemName + " / ", dto, warnings, declaredVariables);
                continue;
            }

            if (!item.TryGetProperty("request", out var request))
            {
                warnings.Add($"{folderPrefix}{itemName}: has no 'request' and is not a folder, skipped.");
                continue;
            }

            var stepName = folderPrefix + itemName;
            if (item.TryGetProperty("event", out var eventsEl) && eventsEl.ValueKind == JsonValueKind.Array && eventsEl.GetArrayLength() > 0)
                warnings.Add($"{stepName}: has pre-request/test scripts — scripts are not imported; add equivalent flow steps manually.");

            var step = BuildStep(stepName, request, warnings, declaredVariables);
            if (step is not null)
                dto.Steps.Add(step);
        }
    }

    private static FlowFileStepDto? BuildStep(string stepName, JsonElement request, List<string> warnings, HashSet<string> declaredVariables)
    {
        var method = request.TryGetProperty("method", out var methodEl) ? methodEl.GetString() ?? "GET" : "GET";

        if (!request.TryGetProperty("url", out var urlEl))
        {
            warnings.Add($"{stepName}: has no URL, skipped.");
            return null;
        }

        var (url, pathParams, queryParams) = BuildUrl(stepName, urlEl, warnings, declaredVariables);
        if (url is null)
            return null;

        var parameters = new Dictionary<string, object?> { ["url"] = url, ["method"] = method.ToUpperInvariant() };
        if (pathParams.Count > 0) parameters["pathParams"] = pathParams;
        if (queryParams.Count > 0) parameters["queryParams"] = queryParams;

        var headers = new Dictionary<string, object?>();
        if (request.TryGetProperty("header", out var headersEl) && headersEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var h in headersEl.EnumerateArray())
            {
                if (h.TryGetProperty("disabled", out var disabledEl) && disabledEl.ValueKind == JsonValueKind.True)
                    continue;
                if (h.TryGetProperty("key", out var keyEl) && keyEl.GetString() is string key
                    && h.TryGetProperty("value", out var valueEl))
                {
                    headers[key] = TranslateVars(valueEl.GetString() ?? "", stepName, warnings, declaredVariables);
                }
            }
        }

        ApplyAuth(stepName, request, headers, warnings, declaredVariables);
        if (headers.Count > 0) parameters["headers"] = headers;

        ApplyBody(stepName, request, parameters, warnings);

        return new FlowFileStepDto { Name = stepName, Module = "api", Action = "request", Params = parameters };
    }

    private static (string? Url, Dictionary<string, object?> PathParams, Dictionary<string, object?> QueryParams) BuildUrl(
        string stepName, JsonElement urlEl, List<string> warnings, HashSet<string> declaredVariables)
    {
        var pathParams = new Dictionary<string, object?>();
        var queryParams = new Dictionary<string, object?>();

        string? raw = urlEl.ValueKind switch
        {
            JsonValueKind.String => urlEl.GetString(),
            JsonValueKind.Object when urlEl.TryGetProperty("raw", out var rawEl) => rawEl.GetString(),
            _ => null,
        };
        if (raw is null)
        {
            warnings.Add($"{stepName}: URL has no usable 'raw' value, skipped.");
            return (null, pathParams, queryParams);
        }

        // A structured `query` array lets a disabled param or a {{var}} inside a query value be
        // handled explicitly, the same as a header's -- rebuilt into queryParams, and the literal
        // query string dropped from the URL itself so it isn't sent twice. Without one (a bare
        // string URL), the query string is kept embedded and still run through the same
        // placeholder/{{var}} handling below -- ApiModule's own `url` param already parses an
        // embedded '?...' query string itself (API-T2), so nothing here is lost or silently
        // dropped either way.
        var queryArrEl = urlEl.ValueKind == JsonValueKind.Object && urlEl.TryGetProperty("query", out var q0) && q0.ValueKind == JsonValueKind.Array
            ? q0
            : (JsonElement?)null;

        string urlPart;
        if (queryArrEl is { } realQueryArrEl)
        {
            var queryStart = raw.IndexOf('?');
            urlPart = queryStart >= 0 ? raw[..queryStart] : raw;

            foreach (var q in realQueryArrEl.EnumerateArray())
            {
                if (q.TryGetProperty("disabled", out var disabledEl) && disabledEl.ValueKind == JsonValueKind.True)
                    continue;
                if (q.TryGetProperty("key", out var keyEl) && keyEl.GetString() is string key && !string.IsNullOrEmpty(key))
                {
                    var value = q.TryGetProperty("value", out var valueEl) ? valueEl.GetString() ?? "" : "";
                    queryParams[key] = TranslateVars(value, stepName, warnings, declaredVariables);
                }
            }
        }
        else
        {
            urlPart = raw;
        }

        // Postman's `:id` path-param syntax -> Yukti's `{id}` ApiModule already resolves via pathParams.
        var withPlaceholders = PathParamPattern.Replace(urlPart, m => $"{{{m.Groups[1].Value}}}");
        foreach (Match m in PathParamPattern.Matches(urlPart))
            pathParams[m.Groups[1].Value] = $"{{{{vars.{m.Groups[1].Value}}}}}";

        var url = TranslateVars(withPlaceholders, stepName, warnings, declaredVariables);
        return (url, pathParams, queryParams);
    }

    private static void ApplyAuth(string stepName, JsonElement request, Dictionary<string, object?> headers, List<string> warnings, HashSet<string> declaredVariables)
    {
        if (!request.TryGetProperty("auth", out var authEl) || !authEl.TryGetProperty("type", out var typeEl))
            return;

        var type = typeEl.GetString();
        switch (type)
        {
            case "bearer":
                if (authEl.TryGetProperty("bearer", out var bearerEl) && bearerEl.ValueKind == JsonValueKind.Array)
                {
                    var token = bearerEl.EnumerateArray()
                        .FirstOrDefault(e => e.TryGetProperty("key", out var k) && k.GetString() == "token");
                    if (token.ValueKind == JsonValueKind.Object && token.TryGetProperty("value", out var tokenValueEl))
                        headers["Authorization"] = "Bearer " + TranslateVars(tokenValueEl.GetString() ?? "", stepName, warnings, declaredVariables);
                }
                break;
            case "basic":
                warnings.Add($"{stepName}: uses Basic auth — Yukti has no credential-secret pipeline yet (blocked, v0.2); add an Authorization header manually.");
                break;
            default:
                warnings.Add($"{stepName}: uses auth type '{type}', which has no Yukti equivalent yet, skipped.");
                break;
        }
    }

    private static void ApplyBody(string stepName, JsonElement request, Dictionary<string, object?> parameters, List<string> warnings)
    {
        if (!request.TryGetProperty("body", out var bodyEl))
            return;

        var mode = bodyEl.TryGetProperty("mode", out var modeEl) ? modeEl.GetString() : null;
        if (mode != "raw")
        {
            if (mode is not null)
                warnings.Add($"{stepName}: body mode '{mode}' has no Yukti equivalent yet, skipped.");
            return;
        }

        if (!bodyEl.TryGetProperty("raw", out var rawEl) || rawEl.GetString() is not string raw || raw.Length == 0)
            return;

        try
        {
            using var parsed = JsonDocument.Parse(raw);
            parameters["body"] = FlowFileJson.Normalize(parsed.RootElement.Clone());
        }
        catch (JsonException)
        {
            // Not JSON -- sent as plain text, same as ApiModule's own default for a string body.
            parameters["body"] = raw;
        }
    }

    private static string TranslateVars(string text, string stepName, List<string> warnings, HashSet<string> declaredVariables)
    {
        return VarPattern.Replace(text, m =>
        {
            var name = m.Groups[1].Value;
            if (name.StartsWith('$'))
            {
                warnings.Add($"{stepName}: uses Postman's dynamic variable '{{{{{name}}}}}', which has no Yukti equivalent, left as-is.");
                return m.Value;
            }
            declaredVariables.Add(name);
            return $"{{{{vars.{name}}}}}";
        });
    }
}
