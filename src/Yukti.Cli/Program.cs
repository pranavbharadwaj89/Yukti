using System.CommandLine;
using Microsoft.Extensions.Logging;
using Yukti.Cli;
using Yukti.Domain.Events;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;

var flowFileArgument = new Argument<FileInfo>("flow-file")
{
    Description = "Path to the flow JSON file to run.",
};

var junitOption = new Option<FileInfo?>("--junit") { Description = "Write a JUnit XML report to this path." };
var jsonOption = new Option<FileInfo?>("--json") { Description = "Write a unified JSON report to this path (same shape as the API's run report)." };
var varOption = new Option<string[]>("--var") { Description = "Initial run variable as key=value. Repeatable." };
var aiOption = new Option<string>("--ai")
{
    Description = "off|on. Default: off. With AI off, no AI-provider client is ever registered, so no outbound call is possible.",
    DefaultValueFactory = _ => "off",
};
var timeoutOption = new Option<int>("--timeout") { Description = "Whole-run limit in seconds.", DefaultValueFactory = _ => 600 };
var verboseOption = new Option<bool>("--verbose") { Description = "Print each step as it completes: status, module.action, duration." };

var runCommand = new Command("run", "Run a flow file in-process. No API server, no Postgres.");
runCommand.Arguments.Add(flowFileArgument);
runCommand.Options.Add(junitOption);
runCommand.Options.Add(jsonOption);
runCommand.Options.Add(varOption);
runCommand.Options.Add(aiOption);
runCommand.Options.Add(timeoutOption);
runCommand.Options.Add(verboseOption);

runCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var flowFile = parseResult.GetValue(flowFileArgument)!;
    var junitPath = parseResult.GetValue(junitOption);
    var jsonPath = parseResult.GetValue(jsonOption);
    var varArgs = parseResult.GetValue(varOption) ?? Array.Empty<string>();
    var ai = parseResult.GetValue(aiOption) ?? "off";
    var timeoutSeconds = parseResult.GetValue(timeoutOption);
    var verbose = parseResult.GetValue(verboseOption);

    if (ai is not ("off" or "on"))
    {
        await Console.Error.WriteLineAsync($"--ai must be 'off' or 'on', got '{ai}'.");
        return 2;
    }
    // D2-T6 scope: the --ai flag only blocks AI (there is no AI-provider
    // integration in this engine yet — that's Day 5). "on" is accepted so
    // the flag's shape is already stable for Day 5 to wire a real meaning
    // into, but it changes nothing today either way.

    var initialVariables = new Dictionary<string, object?>();
    foreach (var arg in varArgs)
    {
        var eq = arg.IndexOf('=');
        if (eq <= 0)
        {
            await Console.Error.WriteLineAsync($"--var must be key=value, got '{arg}'.");
            return 2;
        }
        initialVariables[arg[..eq]] = arg[(eq + 1)..];
    }

    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
    var ct = linkedCts.Token;

    using var loggerFactory = LoggerFactory.Create(logging => logging
        .AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace) // FR-LOG: structured logs to stderr, never stdout
        .SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Warning));

    CliHost host;
    try
    {
        host = await CliHost.Build(loggerFactory, ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        await Console.Error.WriteLineAsync($"Infrastructure error setting up the runtime: {ex.Message}");
        return 3;
    }

    if (verbose)
    {
        host.Dispatcher.Subscribe<StepCompletedEvent>(evt =>
        {
            var icon = evt.Result.Status switch { StepStatus.Passed => "pass", StepStatus.Failed => "FAIL", _ => "skip" };
            var flaky = evt.Result.IsFlaky ? " (flaky)" : "";
            Console.Error.WriteLine($"[{icon}{flaky}] {evt.Result.Module.Value}.{evt.Result.Action} — {evt.Result.StepName} ({evt.Result.Duration.TotalMilliseconds:F0}ms)");
        });
    }

    Flow flow;
    try
    {
        flow = await FlowLoader.LoadAndPublish(flowFile.FullName, host, ct);
    }
    catch (InvalidFlowFileException ex)
    {
        await Console.Error.WriteLineAsync(ex.Message);
        return 2;
    }
    catch (OperationCanceledException)
    {
        await Console.Error.WriteLineAsync("Cancelled or timed out while loading/validating the flow file.");
        return 3;
    }

    FlowRun run;
    Yukti.Domain.SharedKernel.FlowRunId? triggeredRunId = null;
    try
    {
        var runId = await host.TriggerRun.Handle(
            new Yukti.Application.Execution.TriggerFlowRunCommand(flow.Id, RunTrigger.Api, initialVariables, host.TenantId, host.UserId), ct);
        triggeredRunId = runId;
        var triggeredRun = await host.Runs.GetById(runId, ct) ?? throw new InvalidOperationException("Run vanished immediately after being triggered.");
        run = await host.Engine.Execute(flow, triggeredRun, host.Credentials, ct);
    }
    catch (OperationCanceledException)
    {
        // Steps already finished stay in the report — re-read from the
        // repository rather than trust Execute's return value, which never
        // returns on this path (confirmed in FlowEngine's own graceful-
        // shutdown tests: cancellation throws out of the loop, it doesn't
        // return a completed FlowRun).
        var partial = triggeredRunId is { } id ? await host.Runs.GetById(id, CancellationToken.None) : null;
        await Console.Error.WriteLineAsync("Run cancelled or timed out.");
        await WriteReportsIfPossible(flow, partial, junitPath, jsonPath);
        return 3;
    }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync($"Unexpected infrastructure error: {ex.Message}");
        return 3;
    }

    await WriteReportsIfPossible(flow, run, junitPath, jsonPath);

    var passedCount = run.Results.Count(r => r.Status == StepStatus.Passed);
    var failedCount = run.Results.Count(r => r.Status == StepStatus.Failed);
    var skippedCount = run.Results.Count(r => r.Status == StepStatus.Skipped);
    var flakyCount = run.Results.Count(r => r.IsFlaky);
    Console.WriteLine($"{flow.Name}: {run.Status} — {passedCount} passed, {failedCount} failed, {skippedCount} skipped, {flakyCount} flaky");

    return run.Status == RunStatus.Failed ? 1 : 0;

    static async Task WriteReportsIfPossible(Flow flow, FlowRun? run, FileInfo? junitPath, FileInfo? jsonPath)
    {
        if (run is null) return;
        if (junitPath is not null)
            await File.WriteAllTextAsync(junitPath.FullName, JUnitReportWriter.Write(flow, run).ToString());
        if (jsonPath is not null)
            await File.WriteAllTextAsync(jsonPath.FullName, JsonReportWriter.Write(run));
    }
});

var rootCommand = new RootCommand("Yukti — author, validate and run automation flows.");
rootCommand.Subcommands.Add(runCommand);

return await rootCommand.Parse(args).InvokeAsync();
