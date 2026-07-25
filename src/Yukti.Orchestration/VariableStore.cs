using System.Text.RegularExpressions;

namespace Yukti.Orchestration;

/// <summary>
/// A direct, formalized evolution of the {{vars.x.y}} interpolation logic
/// already validated in this project's early prototype. Operates on
/// already-parsed VariableExpression paths at the domain layer for publish-
/// time validation; this runtime component performs the actual per-execution
/// substitution against a FlowRun's live variable scope. (Volume 1 Part III §19.4)
/// </summary>
public sealed partial class VariableStore : IVariableStore
{
    [GeneratedRegex(@"\{\{\s*([a-zA-Z0-9_.]+)\s*\}\}")]
    private static partial Regex TemplatePattern();

    public IReadOnlyDictionary<string, object?> Interpolate(
        IReadOnlyDictionary<string, object?> parameters, IReadOnlyDictionary<string, object?> vars)
    {
        var result = new Dictionary<string, object?>();
        foreach (var (key, value) in parameters)
            result[key] = InterpolateValue(value, vars);
        return result;
    }

    private object? InterpolateValue(object? value, IReadOnlyDictionary<string, object?> vars)
    {
        switch (value)
        {
            case string s:
                var wholeMatch = Regex.Match(s, @"^\{\{\s*([a-zA-Z0-9_.]+)\s*\}\}$");
                if (wholeMatch.Success)
                    return ResolvePath(wholeMatch.Groups[1].Value, vars);

                return TemplatePattern().Replace(s, m =>
                {
                    var resolved = ResolvePath(m.Groups[1].Value, vars);
                    return resolved?.ToString() ?? string.Empty;
                });

            case IReadOnlyDictionary<string, object?> nested:
                var nestedResult = new Dictionary<string, object?>();
                foreach (var (k, v) in nested)
                    nestedResult[k] = InterpolateValue(v, vars);
                return nestedResult;

            case IEnumerable<object?> list when value is not string:
                return list.Select(v => InterpolateValue(v, vars)).ToList();

            default:
                return value;
        }
    }

    private static object? ResolvePath(string path, IReadOnlyDictionary<string, object?> vars)
    {
        var segments = path.Split('.');
        if (segments.Length == 0 || segments[0] != "vars")
            return null;

        object? current = vars;
        foreach (var segment in segments.Skip(1))
        {
            if (current is IReadOnlyDictionary<string, object?> dict && dict.TryGetValue(segment, out var next))
                current = next;
            else
                return null;
        }
        return current;
    }

    public bool EvaluateCondition(string? whenCondition, IReadOnlyDictionary<string, object?> vars)
    {
        if (string.IsNullOrWhiteSpace(whenCondition))
            return true;

        var resolved = ResolvePath(whenCondition.Trim(), vars);
        return resolved switch
        {
            null => false,
            bool b => b,
            string s => !string.IsNullOrEmpty(s) && !s.Equals("false", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }
}
