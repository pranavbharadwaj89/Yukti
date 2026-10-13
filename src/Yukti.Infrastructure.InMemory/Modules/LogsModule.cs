using System.Globalization;
using System.Text.RegularExpressions;
using Yukti.Contracts;
using Yukti.Domain.ModulePlugin;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// Real, working log automation — a declarative rule engine over log text
/// ("fail if ERROR appears more than N times") plus statistical anomaly
/// detection over per-minute error rates. Direct port of the original TS
/// prototype's LogModule to the formal IAutomationModule contract.
/// (Volume 1 Part III §18)
///
/// Logs can come inline (logText) or from a file (logFile). A logFile that
/// doesn't exist fails the step — it never reads as "0 lines, all rules
/// passed". checkRules can be limited to a time window (since/until).
/// Holds no instance state (FR-PLUGIN-07): safe as a singleton across
/// concurrent runs.
/// </summary>
public sealed class LogsModule : IAutomationModule
{
    private const int MaxLinesReturnedPerRule = 50;

    public ModuleKind Kind => ModuleKind.Logs;
    public string ContractVersion => "1.0.0";

    public IReadOnlyList<ActionSchema> GetSupportedActions() => new[]
    {
        new ActionSchema
        {
            ActionName = "checkRules",
            Description = "Runs a declarative rule engine over log lines, failing if any rule's match count exceeds its threshold. Optionally limited to a time window.",
            Parameters = new[]
            {
                new ParamSpec { Name = "logText", Type = ParamType.String, Required = false, Description = "Raw log text to check. Provide this or logFile." },
                new ParamSpec { Name = "logFile", Type = ParamType.String, Required = false, Description = "Path to a log file to read. A missing file fails the step. Provide this or logText." },
                new ParamSpec { Name = "rules", Type = ParamType.Array, Required = true, Description = "Array of {name, pattern?, severity?, maxAllowed}. A line matches when it matches pattern (regex) and contains severity (e.g. ERROR); at least one of the two is required." },
                new ParamSpec { Name = "since", Type = ParamType.String, Required = false, Description = "ISO-8601 timestamp; only lines at or after it are checked." },
                new ParamSpec { Name = "until", Type = ParamType.String, Required = false, Description = "ISO-8601 timestamp; only lines at or before it are checked." },
            }
        },
        new ActionSchema
        {
            ActionName = "detectAnomalies",
            Description = "Buckets log lines by minute and flags buckets whose error rate exceeds mean + N standard deviations.",
            Parameters = new[]
            {
                new ParamSpec { Name = "logText", Type = ParamType.String, Required = false, Description = "Raw log text to bucket and analyze. Provide this or logFile." },
                new ParamSpec { Name = "logFile", Type = ParamType.String, Required = false, Description = "Path to a log file to read. A missing file fails the step. Provide this or logText." },
                new ParamSpec { Name = "stdDevThreshold", Type = ParamType.Number, Required = false, DefaultValue = 2.0, Description = "Number of standard deviations above the mean error rate that flags a bucket as anomalous." },
            }
        }
    };

    public Task Setup(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;
    public Task Teardown(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;

    public async Task<StepOutcome> Run(string action, IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct)
    {
        if (action is not ("checkRules" or "detectAnomalies"))
            return StepOutcome.Failed($"Unknown logs action '{action}'. Supported: checkRules, detectAnomalies.");

        var (lines, source, loadError) = await LoadLines(parameters, ct);
        if (loadError is not null)
            return StepOutcome.Failed(loadError);

        return action == "checkRules"
            ? CheckRules(lines, source, parameters)
            : DetectAnomalies(lines, parameters);
    }

    private static async Task<(string[] Lines, string Source, string? Error)> LoadLines(IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        var logFile = parameters.GetValueOrDefault("logFile") as string;
        if (!string.IsNullOrWhiteSpace(logFile))
        {
            if (!File.Exists(logFile))
                return (Array.Empty<string>(), logFile, $"Log file not found: '{logFile}'.");
            var fileLines = await File.ReadAllLinesAsync(logFile, ct);
            return (fileLines.Where(l => l.Length > 0).ToArray(), logFile, null);
        }

        if (parameters.GetValueOrDefault("logText") is string logText)
            return (logText.Split('\n', StringSplitOptions.RemoveEmptyEntries), "logText", null);

        return (Array.Empty<string>(), "", "Logs step requires either a 'logFile' or a 'logText' parameter.");
    }

    private static StepOutcome CheckRules(string[] lines, string source, IReadOnlyDictionary<string, object?> parameters)
    {
        if (parameters.GetValueOrDefault("rules") is not IEnumerable<object?> rawRules)
            return StepOutcome.Failed("checkRules requires a 'rules' array parameter.");

        DateTimeOffset? since = null;
        DateTimeOffset? until = null;
        if (parameters.GetValueOrDefault("since") is string sinceText)
        {
            if (!TryParseTimestamp(sinceText, out var s))
                return StepOutcome.Failed($"'since' is not a valid ISO-8601 timestamp: '{sinceText}'.");
            since = s;
        }
        if (parameters.GetValueOrDefault("until") is string untilText)
        {
            if (!TryParseTimestamp(untilText, out var u))
                return StepOutcome.Failed($"'until' is not a valid ISO-8601 timestamp: '{untilText}'.");
            until = u;
        }

        var windowed = since is not null || until is not null;
        var linesInWindow = lines;
        var linesWithoutTimestamp = 0;
        if (windowed)
        {
            var kept = new List<string>();
            foreach (var line in lines)
            {
                if (!TryParseLineTimestamp(line, out var ts))
                {
                    linesWithoutTimestamp++;
                    continue;
                }
                if (since is not null && ts < since) continue;
                if (until is not null && ts > until) continue;
                kept.Add(line);
            }
            linesInWindow = kept.ToArray();
        }

        var violations = new List<string>();
        var matches = new List<object?>();

        foreach (var raw in rawRules)
        {
            if (raw is not IReadOnlyDictionary<string, object?> rule)
                return StepOutcome.Failed("Each checkRules rule must be an object {name, pattern?, severity?, maxAllowed}.");

            var name = rule.GetValueOrDefault("name") as string ?? "unnamed-rule";
            var pattern = rule.GetValueOrDefault("pattern") as string;
            var severity = rule.GetValueOrDefault("severity") as string;
            var maxAllowed = Convert.ToInt32(rule.GetValueOrDefault("maxAllowed") ?? 0, CultureInfo.InvariantCulture);

            if (string.IsNullOrEmpty(pattern) && string.IsNullOrEmpty(severity))
                return StepOutcome.Failed($"rule '{name}' needs a 'pattern', a 'severity', or both.");

            Regex? patternRegex;
            try
            {
                patternRegex = string.IsNullOrEmpty(pattern) ? null : new Regex(pattern);
            }
            catch (ArgumentException ex)
            {
                return StepOutcome.Failed($"rule '{name}' has an invalid pattern: {ex.Message}");
            }
            var severityRegex = string.IsNullOrEmpty(severity)
                ? null
                : new Regex($@"\b{Regex.Escape(severity)}\b", RegexOptions.IgnoreCase);

            var hitLines = linesInWindow
                .Where(l => (patternRegex?.IsMatch(l) ?? true) && (severityRegex?.IsMatch(l) ?? true))
                .ToList();

            matches.Add(new Dictionary<string, object?>
            {
                ["rule"] = name,
                ["count"] = hitLines.Count,
                ["maxAllowed"] = maxAllowed,
                ["lines"] = hitLines.Take(MaxLinesReturnedPerRule).ToList()
            });

            if (hitLines.Count > maxAllowed)
                violations.Add($"rule '{name}': {hitLines.Count} matches, max allowed {maxAllowed}");
        }

        var data = new Dictionary<string, object?>
        {
            ["source"] = source,
            ["matches"] = matches,
            ["linesScanned"] = linesInWindow.Length,
            ["linesTotal"] = lines.Length,
            ["linesWithoutTimestamp"] = windowed ? linesWithoutTimestamp : null,
            ["since"] = since?.ToString("O", CultureInfo.InvariantCulture),
            ["until"] = until?.ToString("O", CultureInfo.InvariantCulture)
        };

        var scope = windowed ? $"{linesInWindow.Length} of {lines.Length} lines in window" : $"{lines.Length} lines";
        return violations.Count > 0
            ? StepOutcome.Failed(string.Join("; ", violations), data)
            : StepOutcome.Passed($"{scope} scanned from {source}, all rules within threshold", data);
    }

    private static readonly Regex LeadingTimestamp = new(@"^\[?([0-9]{4}-[0-9]{2}-[0-9]{2}[T ][^\s\]]+)\]?", RegexOptions.Compiled);

    private static bool TryParseLineTimestamp(string line, out DateTimeOffset ts)
    {
        var m = LeadingTimestamp.Match(line);
        if (m.Success) return TryParseTimestamp(m.Groups[1].Value, out ts);
        ts = default;
        return false;
    }

    private static bool TryParseTimestamp(string text, out DateTimeOffset ts) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out ts);

    private static readonly Regex TimestampPattern = new(@"^\[?(\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2})", RegexOptions.Compiled);
    private static readonly Regex ErrorPattern = new("error|fatal|exception", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static StepOutcome DetectAnomalies(string[] lines, IReadOnlyDictionary<string, object?> parameters)
    {
        var stdDevThreshold = Convert.ToDouble(parameters.GetValueOrDefault("stdDevThreshold") ?? 2.0, CultureInfo.InvariantCulture);
        var buckets = new Dictionary<string, (int total, int errors)>();

        foreach (var line in lines)
        {
            var m = TimestampPattern.Match(line);
            var bucket = m.Success ? m.Groups[1].Value : "unknown";
            var (total, errors) = buckets.GetValueOrDefault(bucket, (0, 0));
            total++;
            if (ErrorPattern.IsMatch(line)) errors++;
            buckets[bucket] = (total, errors);
        }

        var rates = buckets.Values.Select(b => b.total > 0 ? (double)b.errors / b.total : 0).ToList();
        var mean = rates.Count > 0 ? rates.Average() : 0;
        var variance = rates.Count > 0 ? rates.Average(r => Math.Pow(r - mean, 2)) : 0;
        var stdDev = Math.Sqrt(variance);
        var threshold = mean + stdDevThreshold * stdDev;

        var anomalous = buckets
            .Where(kv => threshold > 0 && kv.Value.total > 0 && (double)kv.Value.errors / kv.Value.total > threshold)
            .Select(kv => (object?)new Dictionary<string, object?>
            {
                ["bucket"] = kv.Key,
                ["errorRate"] = (double)kv.Value.errors / kv.Value.total,
                ["total"] = kv.Value.total,
                ["errors"] = kv.Value.errors
            })
            .ToList();

        var data = new Dictionary<string, object?>
        {
            ["mean"] = mean,
            ["stdDev"] = stdDev,
            ["threshold"] = threshold,
            ["anomalousBuckets"] = anomalous,
            ["bucketsScanned"] = buckets.Count
        };

        return anomalous.Count > 0
            ? StepOutcome.Failed($"{anomalous.Count} time bucket(s) exceeded {threshold * 100:F1}% error rate", data)
            : StepOutcome.Passed($"Error rate stable across {buckets.Count} buckets (mean {mean * 100:F2}%)", data);
    }
}
