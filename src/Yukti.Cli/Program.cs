using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Yukti.Application.Ai;
using Yukti.Cli;
using Yukti.Cli.Eval;
using Yukti.Cli.Import;
using Yukti.Domain.Events;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Infrastructure.Ai;

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

var sourceFileArgument = new Argument<FileInfo>("source-file") { Description = "Path to the OpenAPI/Postman document to import." };
var outputOption = new Option<FileInfo>("--output", "-o") { Description = "Where to write the generated flow file.", Required = true };
var operationOption = new Option<string[]>("--operation") { Description = "Import only this operationId. Repeatable. Default: every operation in the document." };

var importOpenApiCommand = new Command("openapi", "Generate a flow file from an OpenAPI 3.x JSON document.");
importOpenApiCommand.Arguments.Add(sourceFileArgument);
importOpenApiCommand.Options.Add(outputOption);
importOpenApiCommand.Options.Add(operationOption);
importOpenApiCommand.SetAction(async (parseResult, ct) =>
{
    var sourceFile = parseResult.GetValue(sourceFileArgument)!;
    var outputFile = parseResult.GetValue(outputOption)!;
    var operationIds = parseResult.GetValue(operationOption) ?? Array.Empty<string>();

    var json = await File.ReadAllTextAsync(sourceFile.FullName, ct);
    var result = OpenApiImporter.Import(json, operationIds.Length > 0 ? operationIds : null);
    return WriteImportResult(result, outputFile);
});

var importPostmanCommand = new Command("postman", "Generate a flow file from a Postman Collection v2.1 export.");
importPostmanCommand.Arguments.Add(sourceFileArgument);
importPostmanCommand.Options.Add(outputOption);
importPostmanCommand.SetAction(async (parseResult, ct) =>
{
    var sourceFile = parseResult.GetValue(sourceFileArgument)!;
    var outputFile = parseResult.GetValue(outputOption)!;

    var json = await File.ReadAllTextAsync(sourceFile.FullName, ct);
    var result = PostmanImporter.Import(json);
    return WriteImportResult(result, outputFile);
});

var importCommand = new Command("import", "Generate a flow file from an external API specification.");
importCommand.Subcommands.Add(importOpenApiCommand);
importCommand.Subcommands.Add(importPostmanCommand);

static int WriteImportResult(ImportResult result, FileInfo outputFile)
{
    foreach (var warning in result.Warnings)
        Console.Error.WriteLine($"warning: {warning}");

    if (result.Flow is null)
    {
        Console.Error.WriteLine("Import failed — no flow file was written.");
        return 1;
    }

    if (outputFile.Directory is { Exists: false } outputDir)
        outputDir.Create();
    File.WriteAllText(outputFile.FullName, JsonSerializer.Serialize(result.Flow, FlowFileJson.Options));
    Console.WriteLine($"Wrote {result.Flow.Steps.Count} step(s) to {outputFile.FullName}.");
    return 0;
}

var dryRunOption = new Option<bool>("--dry-run") { Description = "Print the worst-case cost estimate and exit. Makes no provider calls." };
var forceOption = new Option<bool>("--force") { Description = "Re-run even if a stored result already exists for this prompt version + registry hash." };

var flowGenerationCommand = new Command("flow-generation",
    "Evaluate the Analyst/Coder flow-generation pipeline against evals/flow_generation/prompts.json.");
flowGenerationCommand.Options.Add(dryRunOption);
flowGenerationCommand.Options.Add(forceOption);

flowGenerationCommand.SetAction(async (parseResult, ct) =>
{
    var dryRun = parseResult.GetValue(dryRunOption);
    var force = parseResult.GetValue(forceOption);
    var repoRoot = EvalHost.RepoRoot();
    var cases = FlowGenerationEvaluator.LoadPromptCases(repoRoot);

    // §R9: the dry-run path never builds the real host — no DB connection, no Gemini client,
    // no outbound call of any kind, even an accidental one.
    if (dryRun)
    {
        var estimateConfig = EvalHost.BuildForEstimate();
        var estimate = FlowGenerationEvaluator.EstimateCost(estimateConfig, cases);
        PrintEstimate(estimate, estimateConfig.ProviderId);
        Console.WriteLine("Dry run - no provider was called."); // plain hyphen: Windows mangles U+2014 on redirected stdout
        return 0;
    }

    // A cache hit (§R9: unchanged prompt version + unchanged registry) must also cost nothing —
    // computed before the real host (and its DB connection) is ever built.
    var registry = EvalHost.BuildRegistry();
    var registryHash = FlowGenerationEvaluator.ComputeRegistryHash(registry);
    if (!force)
    {
        var reused = FlowGenerationEvaluator.FindReusableResult(repoRoot, Analyst.PromptVersion, Coder.PromptVersion, registryHash);
        if (reused is not null)
        {
            PrintResult(reused, fromCache: true);
            return reused.GatePassed ? 0 : 1;
        }
    }

    using var loggerFactory = LoggerFactory.Create(logging => logging
        .AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace)
        .SetMinimumLevel(LogLevel.Warning));

    using var host = await EvalHost.Build(loggerFactory, ct);

    var estimateForThisRun = FlowGenerationEvaluator.EstimateCost(
        new EvalHost.EstimateConfig(host.CapabilityOptions, host.Pricing, host.ProviderId, host.Registry), cases);
    PrintEstimate(estimateForThisRun, host.ProviderId);

    // §R9: "A real run prints the same estimate first and refuses if it would cross the remaining
    // budget" — checked for real, against the real guard, before a single case runs.
    var budgetCheck = await host.BudgetGuard.CheckAsync(
        EvalHost.EvalTenantId, host.ProviderId, estimateForThisRun.WorstCaseInputTokens, estimateForThisRun.WorstCaseOutputTokens, ct);
    if (!budgetCheck.Allowed)
    {
        // DISC-14: a request-count quota refusal is a different constraint from the dollar cap —
        // must not print the dollar message when that's not actually why this was refused (the
        // real case that surfaced this: cost nowhere near the cap, but the free-tier key's own
        // daily request ceiling already hit).
        var message = budgetCheck.QuotaExceeded
            ? $"AI request quota reached: {budgetCheck.RequestsToday} of {budgetCheck.DailyRequestQuota} requests already made " +
              "today (shared with every other caller of this key, including Sarvaghna's own commit/push-hook AI reviews). Refused before any provider call."
            : $"AI budget reached: ${budgetCheck.SpentUsd:F2} already spent this week, plus this run's own worst-case " +
              $"${budgetCheck.ProjectedUsd:F2}, would exceed the ${budgetCheck.CapUsd:F2} weekly cap. Refused before any provider call.";
        await Console.Error.WriteLineAsync(message);
        return 1;
    }

    try
    {
        var result = await FlowGenerationEvaluator.Run(host, cases, ct);
        var savedPath = FlowGenerationEvaluator.SaveResult(repoRoot, result);
        PrintResult(result, fromCache: false);
        Console.WriteLine($"Result saved to {savedPath}");
        return result.GatePassed ? 0 : 1;
    }
    catch (AiBudgetExceededException ex)
    {
        // Mid-run refusal is a budget event, not a structural failure — never saved as if it were
        // a complete, conclusive evaluation of this prompt version.
        await Console.Error.WriteLineAsync($"AI budget reached mid-run: {ex.Message} This partial run was not saved.");
        return 1;
    }

    static void PrintEstimate(EvalCostEstimate estimate, string providerId)
    {
        Console.WriteLine($"Prompts: {estimate.PromptCount}");
        Console.WriteLine($"Worst-case provider calls: {estimate.WorstCaseProviderCalls}");
        Console.WriteLine($"Worst-case tokens: {estimate.WorstCaseInputTokens} in / {estimate.WorstCaseOutputTokens} out");
        Console.WriteLine($"Worst-case cost: ${estimate.WorstCaseCostUsd:F4} ({providerId})");
    }

    static void PrintResult(EvalRunResult result, bool fromCache)
    {
        Console.WriteLine(fromCache
            ? $"Reusing stored result for {result.AnalystPromptVersion}/{result.CoderPromptVersion}/{result.RegistryHash} (ran {result.RanAtUtc:u})."
            : $"Ran {result.TotalCount}/10 cases.");
        foreach (var c in result.Cases)
        {
            var detail = c.Error is null ? "" : $" - {c.Error}";
            Console.WriteLine($"  [{(c.Passed ? "PASS" : "FAIL")}] {c.Id}{detail}");
        }
        Console.WriteLine($"{result.PassedCount}/{result.TotalCount} passed - gate (8/10): {(result.GatePassed ? "PASSED" : "FAILED")}");
    }
});

var evalCommand = new Command("eval", "Evaluate AI capabilities against their recorded prompt sets.");
evalCommand.Subcommands.Add(flowGenerationCommand);

var rootCommand = new RootCommand("Yukti — author, validate and run automation flows.");
rootCommand.Subcommands.Add(runCommand);
rootCommand.Subcommands.Add(importCommand);
rootCommand.Subcommands.Add(evalCommand);

return await rootCommand.Parse(args).InvokeAsync();
