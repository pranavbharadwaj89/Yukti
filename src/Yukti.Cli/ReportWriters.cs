using System.Text.Json;
using System.Xml.Linq;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;

namespace Yukti.Cli;

/// <summary>
/// Redaction is name-based, not value-based — no built-in module currently
/// resolves a credential into a param value (confirmed: nothing in src/
/// calls ExecutionContext.Credentials.ResolveAsync yet), so there is no
/// resolved secret this could leak today. Kept anyway, matching the spec's
/// explicit requirement and the house rule of never printing raw param
/// values (FlowEngine/ModuleDispatcher's own logging only ever names param
/// keys, never values) — this is the same discipline applied to step
/// params shown in a failure report.
/// </summary>
public static class ParamRedaction
{
    private static readonly string[] SensitiveNameFragments = { "token", "secret", "password", "key", "credential", "auth" };

    public static IReadOnlyDictionary<string, object?> Redact(IReadOnlyDictionary<string, object?> @params) =>
        @params.ToDictionary(
            kv => kv.Key,
            kv => SensitiveNameFragments.Any(f => kv.Key.Contains(f, StringComparison.OrdinalIgnoreCase))
                ? "[REDACTED]"
                : kv.Value);
}

/// <summary>
/// One &lt;testsuite&gt; per flow, one &lt;testcase&gt; per step — the exact
/// mapping the spec defines, built straight from StepResult/RetryHistory
/// (both already real domain members, nothing new added here).
/// </summary>
public static class JUnitReportWriter
{
    public static XDocument Write(Flow flow, FlowRun run)
    {
        var testcases = flow.Steps.OrderBy(s => s.Order).Select((step, i) =>
        {
            var result = run.Results.Count > i ? run.Results[i] : null;
            var testcase = new XElement("testcase",
                new XAttribute("name", step.Name),
                new XAttribute("classname", $"yukti.{step.Module.Value}.{step.Action}"),
                new XAttribute("time", (result?.Duration.TotalSeconds ?? 0).ToString("F3")));

            if (result is null)
            {
                // Never dispatched — the run stopped before reaching this
                // step (fail-fast). JUnit has no "didn't run" state, so this
                // is reported the same way a false `when` is: skipped.
                testcase.Add(new XElement("skipped", new XAttribute("message", "never reached — an earlier step failed and ContinueOnFailure was off")));
                return testcase;
            }

            switch (result.Status)
            {
                case StepStatus.Failed:
                    var paramsExcerpt = string.Join(", ", ParamRedaction.Redact(step.Params).Select(kv => $"{kv.Key}={kv.Value}"));
                    testcase.Add(new XElement("failure",
                        new XAttribute("message", result.Error ?? "Step failed."),
                        new XAttribute("type", $"{step.Module.Value}.{step.Action}"),
                        new XText($"params: {paramsExcerpt}{Environment.NewLine}data excerpt: {Excerpt(result.Data)}")));
                    break;
                case StepStatus.Skipped:
                    testcase.Add(new XElement("skipped", new XAttribute("message", result.Message ?? "when condition was false")));
                    break;
                case StepStatus.Passed when result.IsFlaky:
                    testcase.Add(new XElement("properties",
                        new XElement("property", new XAttribute("name", "flaky"), new XAttribute("value", "true")),
                        new XElement("property", new XAttribute("name", "attempts"), new XAttribute("value", (result.RetryHistory.Count + 1).ToString()))));
                    testcase.Add(new XElement("system-out",
                        string.Join(Environment.NewLine, result.RetryHistory.Select(a =>
                            $"attempt {a.AttemptNumber}: {a.Status} ({a.Duration.TotalMilliseconds:F0}ms){(a.Error is null ? "" : " — " + a.Error)}"))));
                    break;
            }

            return testcase;
        }).ToList();

        var tests = testcases.Count;
        var failures = run.Results.Count(r => r.Status == StepStatus.Failed);
        var skipped = testcases.Count(tc => tc.Element("skipped") is not null);
        var totalTime = run.Results.Sum(r => r.Duration.TotalSeconds);

        var testsuite = new XElement("testsuite",
            new XAttribute("name", flow.Name),
            new XAttribute("tests", tests),
            new XAttribute("failures", failures),
            new XAttribute("skipped", skipped),
            new XAttribute("time", totalTime.ToString("F3")),
            testcases);

        return new XDocument(new XElement("testsuites", testsuite));
    }

    private static string Excerpt(object? data)
    {
        if (data is null) return "(none)";
        var json = JsonSerializer.Serialize(data);
        return json.Length > 300 ? json[..300] + "…" : json;
    }
}

/// <summary>Same shape as the API's FlowRunResponse/StepResultResponse
/// (src/Yukti.Api/Dtos.cs) — the CLI can't reference Yukti.Api itself
/// (that would pull in ASP.NET Core for a console tool), so this mirrors
/// the field shape as a plain data contract, the same way PublishResponse
/// exists only in the Api project today.</summary>
public static class JsonReportWriter
{
    public static string Write(FlowRun run)
    {
        var report = new
        {
            id = run.Id.Value,
            flowId = run.FlowId.Value,
            status = run.Status.ToString(),
            trigger = run.Trigger.ToString(),
            startedAt = run.StartedAt,
            finishedAt = run.FinishedAt,
            results = run.Results.Select(r => new
            {
                id = r.Id.Value,
                stepName = r.StepName,
                module = r.Module.Value,
                action = r.Action,
                status = r.Status.ToString(),
                durationMs = r.Duration.TotalMilliseconds,
                message = r.Message,
                error = r.Error,
                data = r.Data,
                isFlaky = r.IsFlaky,
                retryHistory = r.RetryHistory.Select(a => new
                {
                    attemptNumber = a.AttemptNumber,
                    status = a.Status.ToString(),
                    durationMs = a.Duration.TotalMilliseconds,
                    error = a.Error,
                }),
            }),
        };
        return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    }
}
