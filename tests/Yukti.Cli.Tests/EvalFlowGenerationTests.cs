using Xunit;

namespace Yukti.Cli.Tests;

/// <summary>
/// D5-T8/token-economy spec §R9: process-level tests against the actual built CLI, same
/// black-box pattern <see cref="CliIntegrationTests"/> already uses — never a live Gemini call
/// (§R7), via <c>Ai:Provider=fixture</c> (ADR-0005's existing switch) and <see
/// cref="StubAiProvider"/>'s one-fixture-per-capability shape. Each test gets its own isolated
/// <c>YUKTI_EVAL_RESULTS_DIR</c> temp directory so a stored result from one test, or from a real
/// run, never leaks into another.
/// </summary>
public sealed class EvalFlowGenerationTests : IDisposable
{
    private readonly string _repoRoot = FindRepoRoot();
    private readonly List<string> _tempDirs = new();

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Yukti.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Could not locate repo root (Yukti.sln).");
    }

    private string IsolatedResultsDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"yukti-eval-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public async Task Dry_run_prints_an_estimate_and_makes_no_provider_calls()
    {
        var result = await CliProcess.Run("eval", "flow-generation", "--dry-run");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Prompts: 10", result.Stdout);
        Assert.Contains("Worst-case provider calls:", result.Stdout);
        Assert.Contains("Dry run - no provider was called.", result.Stdout);
    }

    [Fact]
    public async Task Early_stop_triggers_after_the_3rd_structural_failure_and_runs_no_further_cases()
    {
        // Analyst always finds "web" (never refuses); Coder's fixture always names a module
        // outside the candidate set, so Coder's own validator (D5-T5) fails every single case —
        // the first 3 cases in prompts.json (p1-p3) are all non-refusal, so 3 straight structural
        // failures should stop the run before case 4 ever starts.
        var fixtureDir = Path.Combine(_repoRoot, "tests", "Yukti.Cli.Tests", "fixtures", "eval-early-stop");
        var env = new Dictionary<string, string>
        {
            ["Ai__Provider"] = "fixture",
            ["Ai__FixtureDirectory"] = fixtureDir,
            ["YUKTI_EVAL_RESULTS_DIR"] = IsolatedResultsDir(),
        };

        var result = await CliProcess.Run(env, "eval", "flow-generation");

        Assert.Equal(1, result.ExitCode); // gate failed — 0 passes
        Assert.Contains("Ran 3/10 cases.", result.Stdout);
        Assert.Contains("[FAIL] p1", result.Stdout);
        Assert.Contains("[FAIL] p2", result.Stdout);
        Assert.Contains("[FAIL] p3", result.Stdout);
        Assert.DoesNotContain("p4", result.Stdout); // the 4th case never ran — no further calls
        Assert.Contains("0/3 passed", result.Stdout);
    }

    [Fact]
    public async Task An_unchanged_version_and_registry_reuses_the_stored_result_without_force()
    {
        var fixtureDir = Path.Combine(_repoRoot, "tests", "Yukti.Cli.Tests", "fixtures", "eval-reuse");
        var resultsDir = IsolatedResultsDir();
        var env = new Dictionary<string, string>
        {
            ["Ai__Provider"] = "fixture",
            ["Ai__FixtureDirectory"] = fixtureDir,
            ["YUKTI_EVAL_RESULTS_DIR"] = resultsDir,
        };

        var first = await CliProcess.Run(env, "eval", "flow-generation");
        Assert.Equal(0, first.ExitCode); // 8/10 (p9/p10 expect refusal but this fixture never refuses)
        Assert.Contains("Ran 10/10 cases.", first.Stdout);
        Assert.Single(Directory.GetFiles(resultsDir, "*.json"));

        // Re-running without --force must reuse the stored result — 0 provider calls, so the
        // fixture directory doesn't even need to still resolve for this to succeed.
        var second = await CliProcess.Run(env, "eval", "flow-generation");
        Assert.Equal(0, second.ExitCode);
        Assert.Contains("Reusing stored result for", second.Stdout);
        Assert.Single(Directory.GetFiles(resultsDir, "*.json")); // no second file was written

        // --force explicitly asks for a real re-run, producing a second stored result.
        var forced = await CliProcess.Run(env, "eval", "flow-generation", "--force");
        Assert.Equal(0, forced.ExitCode);
        Assert.Contains("Ran 10/10 cases.", forced.Stdout);
        Assert.Equal(2, Directory.GetFiles(resultsDir, "*.json").Length);
    }
}
