using Xunit;

namespace Yukti.Analyzers.Tests;

public sealed class NoSyncOverAsyncAnalyzerTests
{
    [Fact]
    public async Task Flags_TaskOfT_Result()
    {
        const string source = """
            using System.Threading.Tasks;
            class C
            {
                public int DoWork()
                {
                    Task<int> t = Task.FromResult(1);
                    return t.Result;
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnostics(new NoSyncOverAsyncAnalyzer(), source);

        Assert.Contains(diagnostics, d => d.Id == NoSyncOverAsyncAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Flags_Task_Wait()
    {
        const string source = """
            using System.Threading.Tasks;
            class C
            {
                public void DoWork()
                {
                    Task t = Task.CompletedTask;
                    t.Wait();
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnostics(new NoSyncOverAsyncAnalyzer(), source);

        Assert.Contains(diagnostics, d => d.Id == NoSyncOverAsyncAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Allows_await_on_a_Task()
    {
        const string source = """
            using System.Threading.Tasks;
            class C
            {
                public async Task<int> DoWork()
                {
                    Task<int> t = Task.FromResult(1);
                    return await t;
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnostics(new NoSyncOverAsyncAnalyzer(), source);

        Assert.DoesNotContain(diagnostics, d => d.Id == NoSyncOverAsyncAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Ignores_a_Result_property_unrelated_to_Task()
    {
        const string source = """
            class Outcome { public int Result { get; set; } }
            class C
            {
                public int DoWork(Outcome outcome) => outcome.Result;
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnostics(new NoSyncOverAsyncAnalyzer(), source);

        Assert.DoesNotContain(diagnostics, d => d.Id == NoSyncOverAsyncAnalyzer.DiagnosticId);
    }
}
