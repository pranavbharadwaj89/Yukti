using Xunit;

namespace Yukti.Analyzers.Tests;

public sealed class AsyncMethodsRequireCancellationTokenAnalyzerTests
{
    [Fact]
    public async Task Flags_async_method_missing_trailing_CancellationToken()
    {
        const string source = """
            using System.Threading.Tasks;
            class C
            {
                public async Task<int> DoWork(string input)
                {
                    await Task.Yield();
                    return input.Length;
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnostics(new AsyncMethodsRequireCancellationTokenAnalyzer(), source);

        Assert.Contains(diagnostics, d => d.Id == AsyncMethodsRequireCancellationTokenAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Allows_async_method_with_trailing_CancellationToken()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            class C
            {
                public async Task<int> DoWork(string input, CancellationToken ct)
                {
                    await Task.Yield();
                    return input.Length;
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnostics(new AsyncMethodsRequireCancellationTokenAnalyzer(), source);

        Assert.DoesNotContain(diagnostics, d => d.Id == AsyncMethodsRequireCancellationTokenAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Allows_DisposeAsync_with_no_parameters()
    {
        const string source = """
            using System.Threading.Tasks;
            class C
            {
                public async Task DisposeAsync()
                {
                    await Task.Yield();
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnostics(new AsyncMethodsRequireCancellationTokenAnalyzer(), source);

        Assert.DoesNotContain(diagnostics, d => d.Id == AsyncMethodsRequireCancellationTokenAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Ignores_synchronous_methods()
    {
        const string source = """
            class C
            {
                public int DoWork(string input) => input.Length;
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnostics(new AsyncMethodsRequireCancellationTokenAnalyzer(), source);

        Assert.DoesNotContain(diagnostics, d => d.Id == AsyncMethodsRequireCancellationTokenAnalyzer.DiagnosticId);
    }
}
