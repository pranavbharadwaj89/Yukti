using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Yukti.Application.Ai;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.Ai;
using Yukti.Orchestration;

namespace Yukti.Cli.Eval;

/// <summary>
/// D5-T8: the <c>eval flow-generation</c> orchestration — PLAN.md's own 10-prompt gate (≥ 8/10
/// structural validity) plus token-economy spec §R9's spend discipline (dry-run estimate first,
/// early stop once the gate is already unreachable, reuse an unchanged version's stored result
/// rather than re-spending). Pure orchestration logic, no provider/DB specifics — those live in
/// <see cref="EvalHost"/>, so this class is exactly as testable against a stub host as a real one.
/// </summary>
public static class FlowGenerationEvaluator
{
    private const int TotalCases = 10;
    private const int GateThreshold = 8;
    /// <summary>§R9: "once failures make ≥ 8/10 impossible (3 structural failures), stop."
    /// With 10 cases, 3 failures already caps the best possible score at 7 — stop right there.</summary>
    private const int MaxTolerableFailures = 2;

    private static readonly UserId EvalUserId = new(Guid.Parse("eeeeeeee-0000-0000-0000-000000000002"));

    public static IReadOnlyList<EvalPromptCase> LoadPromptCases(string repoRoot)
    {
        var path = Path.Combine(repoRoot, "evals", "flow_generation", "prompts.json");
        var json = File.ReadAllText(path);
        var cases = JsonSerializer.Deserialize<List<EvalPromptCase>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"{path} did not deserialize to a prompt case list.");
        if (cases.Count != TotalCases)
            throw new InvalidOperationException($"{path} must contain exactly {TotalCases} cases, found {cases.Count}.");
        return cases;
    }

    /// <summary>A minified, stable-key-order hash over every registered module's kind + action
    /// names + full ParamSpec — the same shape Coder's own context assembly already builds,
    /// reused here only to detect "did the registry change" for result-reuse, never sent anywhere.</summary>
    public static string ComputeRegistryHash(ModuleRegistry registry)
    {
        var summary = registry.All
            .OrderBy(m => m.Kind.Value, StringComparer.Ordinal)
            .Select(m => new
            {
                kind = m.Kind.Value,
                actions = m.GetSupportedActions()
                    .OrderBy(a => a.ActionName, StringComparer.Ordinal)
                    .Select(a => new
                    {
                        a.ActionName,
                        parameters = a.Parameters
                            .OrderBy(p => p.Name, StringComparer.Ordinal)
                            .Select(p => new { p.Name, Type = p.Type.ToString(), p.Required })
                            .ToArray(),
                    })
                    .ToArray(),
            })
            .ToArray();

        var json = JsonSerializer.Serialize(summary);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    /// <summary>§R9: "prints prompt count × projected tokens × price and exits without calling" —
    /// worst case, from the capabilities' own configured caps (the same ceiling the engine itself
    /// enforces per call), never a guess at what a real prompt would actually use.</summary>
    public static EvalCostEstimate EstimateCost(EvalHost.EstimateConfig config, IReadOnlyList<EvalPromptCase> cases)
    {
        var analystCaps = config.CapabilityOptions.GetFor(Analyst.Capability);
        var coderCaps = config.CapabilityOptions.GetFor(Coder.Capability);
        var maxAttempts = config.CapabilityOptions.MaxValidationRetries + 1;
        var pricing = config.Pricing.GetFor(config.ProviderId);

        var nonRefusalCases = cases.Count(c => !c.ExpectRefusal);
        var refusalCases = cases.Count(c => c.ExpectRefusal);

        // Analyst runs for every case; Coder only for the ones not expected to refuse.
        var analystCalls = cases.Count * maxAttempts;
        var coderCalls = nonRefusalCases * maxAttempts;
        _ = refusalCases; // Analyst-only cases still cost Analyst calls, already counted above.

        var analystInputPerCall = Math.Max(1, analystCaps.MaxInputChars / 4);
        var coderInputPerCall = Math.Max(1, coderCaps.MaxInputChars / 4);

        var totalInputTokens = analystCalls * analystInputPerCall + coderCalls * coderInputPerCall;
        var totalOutputTokens = analystCalls * analystCaps.MaxOutputTokens + coderCalls * coderCaps.MaxOutputTokens;
        var totalCost = pricing.CostFor(totalInputTokens, totalOutputTokens);

        return new EvalCostEstimate(cases.Count, analystCalls + coderCalls, totalInputTokens, totalOutputTokens, totalCost);
    }

    /// <summary>The real (or fixture) run — case by case, in file order, stopping early once the
    /// gate is already unreachable. Never catches <see cref="AiBudgetExceededException"/>: the
    /// caller should see that distinctly from an ordinary structural failure and must not cache a
    /// run the budget guard cut short as if it were a complete, conclusive evaluation.</summary>
    public static async Task<EvalRunResult> Run(EvalHost host, IReadOnlyList<EvalPromptCase> cases, CancellationToken ct)
    {
        var results = new List<EvalCaseResult>();
        var failures = 0;

        foreach (var c in cases)
        {
            var result = await RunOne(host, c, ct);
            results.Add(result);
            if (!result.Passed)
            {
                failures++;
                if (failures > MaxTolerableFailures)
                {
                    break; // §R9 early stop — 8/10 is already impossible, no further provider calls.
                }
            }
        }

        var passedCount = results.Count(r => r.Passed);
        return new EvalRunResult(
            Analyst.PromptVersion, Coder.PromptVersion, ComputeRegistryHash(host.Registry), DateTimeOffset.UtcNow,
            passedCount, results.Count, passedCount >= GateThreshold, results);
    }

    private static async Task<EvalCaseResult> RunOne(EvalHost host, EvalPromptCase c, CancellationToken ct)
    {
        ParsedIntent intent;
        try
        {
            intent = await host.Analyst.AnalyzeAsync(c.Description, EvalHost.EvalTenantId, ct);
        }
        catch (AiBudgetExceededException)
        {
            throw; // never a structural failure — the caller stops the whole run for this reason.
        }
        catch (Exception ex)
        {
            return new EvalCaseResult(c.Id, Passed: false, ActualRefused: false, ModuleCorrect: null, AssertionsCorrect: null, ex.Message);
        }

        var actuallyRefused = intent.CandidateModuleKinds.Count == 0;

        if (c.ExpectRefusal)
        {
            return new EvalCaseResult(c.Id, Passed: actuallyRefused, actuallyRefused, ModuleCorrect: null, AssertionsCorrect: null, Error: null);
        }

        if (actuallyRefused)
        {
            // Should have produced a flow but Analyst refused — a structural failure for this case.
            return new EvalCaseResult(c.Id, Passed: false, ActualRefused: true, ModuleCorrect: null, AssertionsCorrect: null,
                Error: "Expected a generated flow but Analyst refused: " + string.Join("; ", intent.Ambiguities));
        }

        var moduleCorrect = new HashSet<string>(intent.CandidateModuleKinds, StringComparer.Ordinal)
            .SetEquals(c.ExpectedModuleKinds);
        var assertionsCorrect = (intent.Assertions.Count > 0) == c.ExpectAssertions;

        try
        {
            await host.Coder.GenerateAsync(intent, EvalHost.EvalTenantId, EvalUserId, ct);
            // Coder.GenerateAsync already ran Day 2's own Flow.Publish validator internally
            // (D5-T5) — reaching here without an exception IS structural validity, not a second
            // copy of that check.
            return new EvalCaseResult(c.Id, Passed: true, ActualRefused: false, moduleCorrect, assertionsCorrect, Error: null);
        }
        catch (AiBudgetExceededException)
        {
            throw;
        }
        catch (AiGenerationFailedException ex)
        {
            return new EvalCaseResult(c.Id, Passed: false, ActualRefused: false, moduleCorrect, assertionsCorrect,
                "Coder failed validation: " + string.Join("; ", ex.ValidationErrors));
        }
        catch (Exception ex)
        {
            return new EvalCaseResult(c.Id, Passed: false, ActualRefused: false, moduleCorrect, assertionsCorrect, ex.Message);
        }
    }

    /// <summary><c>YUKTI_EVAL_RESULTS_DIR</c> lets tests redirect storage to an isolated temp
    /// directory — real results in <c>evals/flow_generation/results/</c> must never be polluted
    /// by a test run, and a test must never see another test's (or a real run's) stale cache hit.</summary>
    public static string ResultsDirectory(string repoRoot) =>
        Environment.GetEnvironmentVariable("YUKTI_EVAL_RESULTS_DIR")
        ?? Path.Combine(repoRoot, "evals", "flow_generation", "results");

    /// <summary>§R9: "re-running an unchanged prompt version with an unchanged registry hash
    /// reuses stored results unless --force." Keyed on exactly those three — a changed prompt file
    /// or a changed module schema must never silently reuse a stale verdict.</summary>
    public static EvalRunResult? FindReusableResult(string repoRoot, string analystVersion, string coderVersion, string registryHash)
    {
        var dir = ResultsDirectory(repoRoot);
        if (!Directory.Exists(dir)) return null;

        var prefix = $"{analystVersion}_{coderVersion}_{registryHash}_";
        var latest = Directory.GetFiles(dir, prefix + "*.json").OrderByDescending(f => f, StringComparer.Ordinal).FirstOrDefault();
        if (latest is null) return null;

        return JsonSerializer.Deserialize<EvalRunResult>(File.ReadAllText(latest));
    }

    public static string SaveResult(string repoRoot, EvalRunResult result)
    {
        var dir = ResultsDirectory(repoRoot);
        Directory.CreateDirectory(dir);
        var fileName = $"{result.AnalystPromptVersion}_{result.CoderPromptVersion}_{result.RegistryHash}_{result.RanAtUtc:yyyyMMddTHHmmssZ}.json";
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }
}
