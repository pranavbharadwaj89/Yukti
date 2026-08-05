using System.Text.Json;

namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// Minimal dotted-path (+ [index]) evaluator over System.Text.Json's
/// JsonElement — deliberately not a full JSONPath implementation (no `$`,
/// wildcards, filters); "items[0].id" is as far as ApiModule's
/// PathEqualsAssertion/PathContainsAssertion/PathExistsAssertion need to
/// reach into a parsed response body.
/// </summary>
internal static class JsonPathEvaluator
{
    public static bool TryGetByPath(JsonElement root, string path, out JsonElement result)
    {
        var current = root;
        foreach (var segment in ParseSegments(path))
        {
            if (segment.IsIndex)
            {
                if (current.ValueKind != JsonValueKind.Array || segment.Index < 0 || segment.Index >= current.GetArrayLength())
                {
                    result = default;
                    return false;
                }
                current = current[segment.Index];
            }
            else
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment.Name!, out var next))
                {
                    result = default;
                    return false;
                }
                current = next;
            }
        }

        result = current;
        return true;
    }

    /// <summary>
    /// Converts a JsonElement subtree into plain CLR types (string, long/double,
    /// bool, null, Dictionary&lt;string,object?&gt;, List&lt;object?&gt;) — the
    /// same shape Yukti.Api's JsonParamNormalizer produces for step params, so
    /// a response body reads the same way whether it arrived as a request
    /// param or as a parsed HTTP response.
    /// </summary>
    public static object? ToPlainValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => ToPlainValue(p.Value)),
        JsonValueKind.Array => element.EnumerateArray().Select(ToPlainValue).ToList(),
        _ => null,
    };

    private readonly record struct Segment(string? Name, int Index, bool IsIndex);

    private static IEnumerable<Segment> ParseSegments(string path)
    {
        foreach (var rawSegment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var remaining = rawSegment;
            var bracketIdx = remaining.IndexOf('[');
            if (bracketIdx < 0)
            {
                yield return new Segment(remaining, 0, false);
                continue;
            }

            if (bracketIdx > 0)
                yield return new Segment(remaining[..bracketIdx], 0, false);

            var rest = remaining[bracketIdx..];
            while (rest.Length > 0 && rest[0] == '[')
            {
                var close = rest.IndexOf(']');
                if (close < 0 || !int.TryParse(rest[1..close], out var idx))
                    yield break; // malformed trailing segment — stop, TryGetByPath's caller sees a partial/failed path

                yield return new Segment(null, idx, true);
                rest = rest[(close + 1)..];
            }
        }
    }
}
