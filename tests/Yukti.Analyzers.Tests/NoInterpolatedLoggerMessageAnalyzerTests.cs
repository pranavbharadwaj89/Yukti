using Xunit;

namespace Yukti.Analyzers.Tests;

public sealed class NoInterpolatedLoggerMessageAnalyzerTests
{
    private const string Preamble = """
        using Microsoft.Extensions.Logging;
        class C
        {
            void M(ILogger logger, string flowRunId, int count)
            {
        """;

    private const string Postamble = """
            }
        }
        """;

    [Fact]
    public async Task Flags_interpolated_string_in_LogInformation()
    {
        var source = Preamble + """
                    logger.LogInformation($"Run {flowRunId} completed with {count} steps");
        """ + Postamble;

        var diagnostics = await AnalyzerTestHarness.GetDiagnostics(new NoInterpolatedLoggerMessageAnalyzer(), source);

        Assert.Contains(diagnostics, d => d.Id == NoInterpolatedLoggerMessageAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Flags_string_concatenation_in_LogError()
    {
        var source = Preamble + """
                    logger.LogError("Run " + flowRunId + " failed");
        """ + Postamble;

        var diagnostics = await AnalyzerTestHarness.GetDiagnostics(new NoInterpolatedLoggerMessageAnalyzer(), source);

        Assert.Contains(diagnostics, d => d.Id == NoInterpolatedLoggerMessageAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Flags_interpolated_string_in_BeginScope()
    {
        var source = Preamble + """
                    using var scope = logger.BeginScope($"FlowRun {flowRunId}");
        """ + Postamble;

        var diagnostics = await AnalyzerTestHarness.GetDiagnostics(new NoInterpolatedLoggerMessageAnalyzer(), source);

        Assert.Contains(diagnostics, d => d.Id == NoInterpolatedLoggerMessageAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Allows_structured_template_with_placeholders()
    {
        var source = Preamble + """
                    logger.LogInformation("Run {FlowRunId} completed with {Count} steps", flowRunId, count);
        """ + Postamble;

        var diagnostics = await AnalyzerTestHarness.GetDiagnostics(new NoInterpolatedLoggerMessageAnalyzer(), source);

        Assert.DoesNotContain(diagnostics, d => d.Id == NoInterpolatedLoggerMessageAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Allows_plain_string_literal_with_no_placeholders()
    {
        var source = Preamble + """
                    logger.LogInformation("FlowRun started");
        """ + Postamble;

        var diagnostics = await AnalyzerTestHarness.GetDiagnostics(new NoInterpolatedLoggerMessageAnalyzer(), source);

        Assert.DoesNotContain(diagnostics, d => d.Id == NoInterpolatedLoggerMessageAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Ignores_interpolated_strings_unrelated_to_ILogger()
    {
        var source = Preamble + """
                    var message = $"Run {flowRunId} completed with {count} steps";
        """ + Postamble;

        var diagnostics = await AnalyzerTestHarness.GetDiagnostics(new NoInterpolatedLoggerMessageAnalyzer(), source);

        Assert.DoesNotContain(diagnostics, d => d.Id == NoInterpolatedLoggerMessageAnalyzer.DiagnosticId);
    }
}
