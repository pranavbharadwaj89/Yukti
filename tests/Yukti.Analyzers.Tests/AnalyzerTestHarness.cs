using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Yukti.Analyzers.Tests;

/// <summary>
/// Compiles a source snippet against Microsoft.Extensions.Logging.Abstractions
/// and runs a given analyzer over it, without pulling in the much larger
/// Microsoft.CodeAnalysis.Testing package family — this project only ever
/// needs "does diagnostic YUKTI001 fire, and where."
/// </summary>
internal static class AnalyzerTestHarness
{
    public static async Task<ImmutableArray<Diagnostic>> GetDiagnostics(
        DiagnosticAnalyzer analyzer, string source)
    {
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Logging.ILogger).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Logging.LoggerExtensions).Assembly.Location),
        };

        var compilation = CSharpCompilation.Create(
            "AnalyzerTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer));
        return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
