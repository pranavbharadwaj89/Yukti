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
/// </summary>
public sealed class LogsModule : IAutomationModule
{
    public ModuleKind Kind => ModuleKind.Logs;
    public string ContractVersion => "1.0.0";

    public IReadOnlyList<ActionSchema> GetSupportedActions() => new[]
    {
        new ActionSchema
        {
            ActionName = "checkRules",
            Description = "Runs a declarative regex rule engine over log text, failing if any rule's match count exceeds its threshold.",
            Parameters = new[]
            {
                new ParamSpec { Name = "logText", Type = ParamType.String, Required = true },
                new ParamSpec { Name = "rules", Type = ParamType.Array, Required = true, Description = "Array of {name, pattern, maxAllowed}." },
            }
        },
        new ActionSchema
        {
            ActionName = "detectAnomalies",
            Description = "Buckets log lines by minute and flags buckets whose error rate exceeds mean + N standard deviations.",
            Parameters = new[]
            {
                new ParamSpec { Name = "logText", Type = ParamType.String, Required = true },
                new ParamSpec { Name = "stdDevThreshold", Type = ParamType.Number, Required = false, DefaultValue = 2.0 },
            }
        }
    };

    public Task Setup(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;
    public Task Teardown(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;

    public Task<StepOutcome> Run(string action, IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct)
    {
        var logText = parameters.GetValueOrDefault("logText") as string ?? "";
        var lines = logText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        return action switch
        {
            "checkRules" => Task.FromResult(CheckRules(lines, parameters)),
            "detectAnomalies" => Task.FromResult(DetectAnomalies(lines, parameters)),
            _ => Task.FromResult(StepOutcome.Failed($"Unknown logs action '{action}'. Supported: checkRules, detectAnomalies."))
        };
    }

    private static StepOutcome CheckRules(string[] lines, IReadOnlyDictionary<string, object?> parameters)
    {
        if (parameters.GetValueOrDefault("rules") is not IEnumerable<object?> rawRules)
            return StepOutcome.Failed("checkRules requires a 'rules' array parameter.");

        var violations = new List<string>();
        var matches = new List<object>();

        foreach (var raw in rawRules)
        {
            if (raw is not IReadOnlyDictionary<string, object?> rule) continue;
            var name = rule.GetValueOrDefault("name") as string ?? "unnamed-rule";
            var pattern = rule.GetValueOrDefault("pattern") as string ?? "";
            var maxAllowed = Convert.ToInt32(rule.GetValueOrDefault("maxAllowed") ?? 0);

            var regex = new Regex(pattern);
            var hitLines = lines.Where(l => regex.IsMatch(l)).ToList();
            matches.Add(new { rule = name, count = hitLines.Count, samples = hitLines.Take(3) });

            if (hitLines.Count > maxAllowed)
                violations.Add($"rule '{name}': {hitLines.Count} matches, max allowed {maxAllowed}");
        }

        var data = new Dictionary<string, object?> { ["matches"] = matches, ["linesScanned"] = lines.Length };
        return violations.Count > 0
            ? StepOutcome.Failed(string.Join("; ", violations), data)
            : StepOutcome.Passed($"{lines.Length} lines scanned, all rules within threshold", data);
    }

    private static readonly Regex TimestampPattern = new(@"^\[?(\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2})", RegexOptions.Compiled);
    private static readonly Regex ErrorPattern = new("error|fatal|exception", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static StepOutcome DetectAnomalies(string[] lines, IReadOnlyDictionary<string, object?> parameters)
    {
        var stdDevThreshold = Convert.ToDouble(parameters.GetValueOrDefault("stdDevThreshold") ?? 2.0);
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
            .Select(kv => new { bucket = kv.Key, errorRate = (double)kv.Value.errors / kv.Value.total, total = kv.Value.total, errors = kv.Value.errors })
            .ToList();

        var data = new Dictionary<string, object?>
        {
            ["mean"] = mean, ["stdDev"] = stdDev, ["threshold"] = threshold,
            ["anomalousBuckets"] = anomalous, ["bucketsScanned"] = buckets.Count
        };

        return anomalous.Count > 0
            ? StepOutcome.Failed($"{anomalous.Count} time bucket(s) exceeded {threshold * 100:F1}% error rate", data)
            : StepOutcome.Passed($"Error rate stable across {buckets.Count} buckets (mean {mean * 100:F2}%)", data);
    }
}
