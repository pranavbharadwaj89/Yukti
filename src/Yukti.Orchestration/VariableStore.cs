using System.Text;
using System.Text.RegularExpressions;
using Yukti.Contracts;

namespace Yukti.Orchestration;

/// <summary>
/// A direct, formalized evolution of the {{vars.x.y}} interpolation logic
/// already validated in this project's early prototype. Operates on
/// already-parsed VariableExpression paths at the domain layer for publish-
/// time validation; this runtime component performs the actual per-execution
/// substitution against a FlowRun's live variable scope. (Volume 1 Part III §19.4)
///
/// API Testing Module Spec, API-T4: also resolves {{secrets.name}}, through
/// the credential resolver FlowEngine already has for exactly this (ADR: no
/// new dependency, no new Vault code — ICredentialResolver/VaultCredentialResolver
/// already existed; this is the one piece that was missing, the {{secrets}}
/// syntax itself). A resolved secret is returned only in the per-dispatch
/// parameter set handed to the module — never written into the run's own
/// Variables — so it can never leak back out through a later {{vars...}}
/// reference or a saveAs.
/// </summary>
public sealed partial class VariableStore : IVariableStore
{
    [GeneratedRegex(@"\{\{\s*([a-zA-Z0-9_.]+)\s*\}\}")]
    private static partial Regex TemplatePattern();

    public async Task<IReadOnlyDictionary<string, object?>> Interpolate(
        IReadOnlyDictionary<string, object?> parameters, IReadOnlyDictionary<string, object?> vars,
        ICredentialResolver credentials, CancellationToken ct)
    {
        var result = new Dictionary<string, object?>();
        foreach (var (key, value) in parameters)
            result[key] = await InterpolateValue(value, vars, credentials, ct);
        return result;
    }

    private async Task<object?> InterpolateValue(
        object? value, IReadOnlyDictionary<string, object?> vars, ICredentialResolver credentials, CancellationToken ct)
    {
        switch (value)
        {
            case string s:
                var wholeMatch = Regex.Match(s, @"^\{\{\s*([a-zA-Z0-9_.]+)\s*\}\}$");
                if (wholeMatch.Success)
                    return await ResolveReference(wholeMatch.Groups[1].Value, vars, credentials, ct);

                var matches = TemplatePattern().Matches(s);
                if (matches.Count == 0)
                    return s;

                // A MatchEvaluator (Regex.Replace's callback) can't await, since {{secrets.name}}
                // resolution is genuinely async (a real Vault call) -- so embedded references are
                // substituted manually, one at a time, in the order they appear.
                var sb = new StringBuilder();
                var lastIndex = 0;
                foreach (Match m in matches)
                {
                    sb.Append(s, lastIndex, m.Index - lastIndex);
                    var resolved = await ResolveReference(m.Groups[1].Value, vars, credentials, ct);
                    sb.Append(resolved?.ToString() ?? string.Empty);
                    lastIndex = m.Index + m.Length;
                }
                sb.Append(s, lastIndex, s.Length - lastIndex);
                return sb.ToString();

            case IReadOnlyDictionary<string, object?> nested:
                var nestedResult = new Dictionary<string, object?>();
                foreach (var (k, v) in nested)
                    nestedResult[k] = await InterpolateValue(v, vars, credentials, ct);
                return nestedResult;

            case IEnumerable<object?> list when value is not string:
                var listResult = new List<object?>();
                foreach (var v in list)
                    listResult.Add(await InterpolateValue(v, vars, credentials, ct));
                return listResult;

            default:
                return value;
        }
    }

    private static async Task<object?> ResolveReference(
        string path, IReadOnlyDictionary<string, object?> vars, ICredentialResolver credentials, CancellationToken ct)
    {
        var segments = path.Split('.');
        if (segments.Length > 0 && segments[0] == "secrets")
        {
            // Secrets are flat string values, never nested objects -- "secrets.name" is the only
            // valid shape; "secrets.name.sub" has nothing to walk into and is simply unresolved.
            if (segments.Length != 2)
                throw new VariableResolutionException(path);

            var name = segments[1];
            var resolved = await credentials.ResolveAsync(name, ct);
            if (resolved is null)
                throw new SecretResolutionException(name);
            return resolved;
        }

        if (!TryResolvePath(path, vars, out var value))
            throw new VariableResolutionException(path);
        return value;
    }

    /// <summary>
    /// False means "not found" (no step ever produced this path) —
    /// distinct from "found, and its value happens to be null" — so
    /// interpolation can throw on the former and never on the latter.
    /// </summary>
    private static bool TryResolvePath(string path, IReadOnlyDictionary<string, object?> vars, out object? value)
    {
        value = null;
        var segments = path.Split('.');
        if (segments.Length == 0 || segments[0] != "vars")
            return false;

        object? current = vars;
        foreach (var segment in segments.Skip(1))
        {
            if (current is IReadOnlyDictionary<string, object?> dict && dict.TryGetValue(segment, out var next))
                current = next;
            else
                return false;
        }
        value = current;
        return true;
    }

    public bool EvaluateCondition(string? whenCondition, IReadOnlyDictionary<string, object?> vars)
    {
        if (string.IsNullOrWhiteSpace(whenCondition))
            return true;

        // A `when` referencing a path no earlier step produced is treated as
        // falsy, not an error — unlike Interpolate, a condition legitimately
        // asks "is this present and truthy", and "absent" is itself a valid,
        // meaningful answer (Skipped), never a run-breaking failure.
        var resolved = TryResolvePath(whenCondition.Trim(), vars, out var found) ? found : null;
        return resolved switch
        {
            null => false,
            bool b => b,
            string s => !string.IsNullOrEmpty(s) && !s.Equals("false", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }
}
