using System.Xml.Linq;
using Xunit;

namespace Yukti.Cli.Tests;

/// <summary>
/// D2-T6: process-level tests against the actual built CLI (a separate
/// `dotnet Yukti.Cli.dll` process each time), never an in-process call into
/// Program's internals — proving the thing a real CI pipeline would
/// actually invoke. Requires `dotnet build` to have run first.
/// </summary>
public sealed class CliIntegrationTests : IDisposable
{
    private readonly StubHttpServer _stub = new();
    private readonly string _repoRoot = FindRepoRoot();
    private readonly List<string> _tempFiles = new();

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Yukti.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Could not locate repo root (Yukti.sln).");
    }

    private string TempPath(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), $"yukti-cli-test-{Guid.NewGuid():N}-{name}");
        _tempFiles.Add(path);
        return path;
    }

    private string WriteFlow(string json)
    {
        var path = TempPath("flow.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public async Task A_passing_flow_exits_0_and_writes_valid_JUnit_with_3_testcases()
    {
        var junitPath = TempPath("out.xml");
        var demoPath = Path.Combine(_repoRoot, "demo", "api_chain.json");

        var result = await CliProcess.Run("run", demoPath, "--var", $"baseUrl={_stub.BaseUrl}", "--junit", junitPath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(junitPath));
        var doc = XDocument.Load(junitPath); // throws on invalid XML
        var testcases = doc.Descendants("testcase").ToList();
        Assert.Equal(3, testcases.Count);
        Assert.Equal("0", doc.Descendants("testsuite").Single().Attribute("failures")!.Value);
    }

    [Fact]
    public async Task A_failing_assertion_exits_1_with_one_failure_in_the_JUnit_output()
    {
        var flowPath = WriteFlow("""
        {
          "name": "Failing assertion flow",
          "declaredVariables": ["baseUrl"],
          "steps": [
            { "name": "Bad status", "module": "api", "action": "request",
              "params": { "url": "{{vars.baseUrl}}/items", "method": "POST", "body": {"name":"x"},
                          "assert": [{"type":"status","expectedStatus": 999}] } }
          ]
        }
        """);
        var junitPath = TempPath("out.xml");

        var result = await CliProcess.Run("run", flowPath, "--var", $"baseUrl={_stub.BaseUrl}", "--junit", junitPath);

        Assert.Equal(1, result.ExitCode);
        var doc = XDocument.Load(junitPath);
        Assert.Single(doc.Descendants("failure"));
    }

    [Fact]
    public async Task An_unregistered_action_exits_2_naming_the_step_on_stderr()
    {
        var flowPath = WriteFlow("""
        {
          "name": "Bad action flow",
          "steps": [
            { "name": "Calls a made-up action", "module": "api", "action": "thisActionDoesNotExist", "params": {} }
          ]
        }
        """);

        var result = await CliProcess.Run("run", flowPath);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Calls a made-up action", result.Stderr);
        Assert.Contains("unresolved_module_action", result.Stderr);
    }

    [Fact]
    public async Task Invalid_JSON_exits_2()
    {
        var flowPath = TempPath("flow.json");
        File.WriteAllText(flowPath, "{ this is not valid json");
        _tempFiles.Add(flowPath);

        var result = await CliProcess.Run("run", flowPath);

        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public async Task A_missing_flow_file_exits_2()
    {
        var result = await CliProcess.Run("run", Path.Combine(_repoRoot, "demo", "does_not_exist.json"));

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("not found", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_step_that_fails_once_then_passes_is_reported_flaky_and_the_run_still_exits_0()
    {
        // Exercises the engine's real default retry policy (MaxAttempts: 2)
        // through the real ApiModule and the stub's own call counter — no
        // test-only module exists inside the CLI's fixed process, so
        // flakiness has to be induced from outside, same as it would be
        // against a genuinely flaky real dependency.
        var flowPath = WriteFlow("""
        {
          "name": "Flaky flow",
          "declaredVariables": ["baseUrl"],
          "steps": [
            { "name": "Flaky call", "module": "api", "action": "request",
              "params": { "url": "{{vars.baseUrl}}/flaky", "method": "GET",
                          "assert": [{"type":"status","expectedStatus": 200}] } }
          ]
        }
        """);
        var junitPath = TempPath("out.xml");

        var result = await CliProcess.Run("run", flowPath, "--var", $"baseUrl={_stub.BaseUrl}", "--junit", junitPath);

        Assert.Equal(0, result.ExitCode);
        var doc = XDocument.Load(junitPath);
        var testcase = doc.Descendants("testcase").Single();
        var flakyProperty = testcase.Descendants("property").FirstOrDefault(p => p.Attribute("name")?.Value == "flaky");
        Assert.NotNull(flakyProperty);
        Assert.Equal("true", flakyProperty!.Attribute("value")!.Value);
    }

    [Fact]
    public async Task A_secret_env_var_never_appears_in_stdout_stderr_or_the_JUnit_file()
    {
        const string secretValue = "sk-test-super-secret-sentinel-value-should-never-leak";
        var junitPath = TempPath("out.xml");
        var demoPath = Path.Combine(_repoRoot, "demo", "api_chain.json");

        var result = await CliProcess.Run(
            new Dictionary<string, string> { ["YUKTI_SECRET_TOKEN"] = secretValue },
            "run", demoPath, "--var", $"baseUrl={_stub.BaseUrl}", "--junit", junitPath, "--verbose");

        Assert.DoesNotContain(secretValue, result.Stdout);
        Assert.DoesNotContain(secretValue, result.Stderr);
        Assert.DoesNotContain(secretValue, File.ReadAllText(junitPath));
    }

    [Fact]
    public async Task A_run_exceeding_the_whole_run_timeout_exits_3()
    {
        var flowPath = WriteFlow("""
        {
          "name": "Slow flow",
          "declaredVariables": ["baseUrl"],
          "steps": [
            { "name": "Takes 5 seconds", "module": "api", "action": "request",
              "params": { "url": "{{vars.baseUrl}}/slow", "method": "GET" } }
          ]
        }
        """);

        var result = await CliProcess.Run("run", flowPath, "--var", $"baseUrl={_stub.BaseUrl}", "--timeout", "1");

        Assert.Equal(3, result.ExitCode);
    }

    public void Dispose()
    {
        _stub.Dispose();
        foreach (var file in _tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { /* best-effort cleanup */ }
        }
    }
}
