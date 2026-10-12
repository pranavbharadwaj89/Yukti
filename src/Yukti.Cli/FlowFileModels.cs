using System.Text.Json;
using System.Text.Json.Serialization;

namespace Yukti.Cli;

/// <summary>
/// The flow-file JSON contract. Deliberately a thin DTO, not the domain
/// Flow/FlowStep types themselves — a flow file is an external, versioned
/// contract (what a user hand-writes or a future AI-generation feature
/// emits), while Flow/FlowStep are the internal aggregate. FlowLoader is
/// what bridges the two, through the same CreateFlowCommand/
/// AddFlowStepCommand handlers the real API uses — never a parallel path.
/// </summary>
public sealed class FlowFileStepDto
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("module")] public string Module { get; set; } = "";
    [JsonPropertyName("action")] public string Action { get; set; } = "";
    [JsonPropertyName("params")] public Dictionary<string, object?> Params { get; set; } = new();
    [JsonPropertyName("saveAs")] public string? SaveAs { get; set; }
    [JsonPropertyName("when")] public string? When { get; set; }
}

public sealed class FlowFileDto
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("continueOnFailure")] public bool ContinueOnFailure { get; set; }
    /// <summary>Names only — the values come from --var at run time (or
    /// the flow's own default, applied if --var omits that name). Declaring
    /// them here is what lets Publish treat {{vars.name}} as already
    /// available instead of "undeclared variable".</summary>
    [JsonPropertyName("declaredVariables")] public List<string> DeclaredVariables { get; set; } = new();
    [JsonPropertyName("steps")] public List<FlowFileStepDto> Steps { get; set; } = new();
}

public static class FlowFileJson
{
    // Disallow: an unknown property (typo, wrong field name) is a flow-file
    // bug the author needs to see, never something to silently ignore —
    // the spec's own explicit requirement.
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>
    /// System.Text.Json deserializes Dictionary&lt;string, object?&gt; values as
    /// boxed JsonElements by default — useless to VariableStore/modules,
    /// which pattern-match on real string/double/bool/Dictionary/List. This
    /// walks the whole params tree once at load time into the plain CLR
    /// object graph every module and VariableStore already expect.
    /// </summary>
    public static object? Normalize(object? value)
    {
        if (value is not JsonElement element)
            return value;

        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => Normalize(p.Value)),
            JsonValueKind.Array => element.EnumerateArray().Select(e => Normalize(e)).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => null,
        };
    }

    public static Dictionary<string, object?> NormalizeParams(Dictionary<string, object?> raw) =>
        raw.ToDictionary(kv => kv.Key, kv => Normalize(kv.Value));
}
