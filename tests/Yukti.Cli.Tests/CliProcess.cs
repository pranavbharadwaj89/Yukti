using System.Diagnostics;

namespace Yukti.Cli.Tests;

public sealed record CliResult(int ExitCode, string Stdout, string Stderr);

/// <summary>
/// Runs the actual built CLI as a separate process — black-box, matching
/// what a real CI pipeline invokes, not an in-process method call against
/// Program's internals.
/// </summary>
public static class CliProcess
{
    private static readonly string DllPath = FindCliDll();

    private static string FindCliDll()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Yukti.sln")))
            dir = Path.GetDirectoryName(dir);
        if (dir is null)
            throw new InvalidOperationException("Could not locate repo root (Yukti.sln) from " + AppContext.BaseDirectory);

        var dll = Path.Combine(dir, "src", "Yukti.Cli", "bin", "Debug", "net8.0", "Yukti.Cli.dll");
        if (!File.Exists(dll))
            throw new InvalidOperationException($"Yukti.Cli.dll not found at '{dll}' — build the solution before running these tests.");
        return dll;
    }

    public static Task<CliResult> Run(params string[] args) => Run(env: null, args);

    public static async Task<CliResult> Run(IReadOnlyDictionary<string, string>? env, params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(DllPath);
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        if (env is not null)
            foreach (var (key, value) in env)
                psi.Environment[key] = value;

        using var process = new Process { StartInfo = psi };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        // A generous outer safety net for the test harness itself — not the
        // mechanism under test. A test that exercises the CLI's own
        // --timeout flag waits on the CLI to self-terminate well inside this.
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return new CliResult(process.ExitCode, stdout, stderr);
    }
}
