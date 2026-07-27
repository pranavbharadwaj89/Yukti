using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Yukti.Analyzers;

/// <summary>
/// FR-STD-02 (Volume 1 Part VI §40.5): every async I/O method accepts and
/// honors a trailing CancellationToken. Flags any method returning
/// System.Threading.Tasks.Task or Task&lt;T&gt; whose last parameter is not
/// a CancellationToken — covers both concrete methods (bodies) and
/// interface/abstract declarations, since the contract belongs at the
/// interface as much as the implementation. Deliberately does not attempt
/// to verify the token is actually *honored* (threaded through to the
/// awaited calls below) — that needs data-flow analysis beyond what a
/// syntax-level rule can cheaply and reliably check; presence at the
/// signature boundary is the enforceable, static half of the requirement.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AsyncMethodsRequireCancellationTokenAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "YUKTI003";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Async I/O methods must accept a trailing CancellationToken",
        messageFormat: "'{0}' returns a Task but its last parameter is not a CancellationToken",
        category: "AsyncStandards",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "FR-STD-02: every async I/O method must accept and honor a trailing CancellationToken.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    private static readonly ImmutableHashSet<string> ExemptMethodNames = ImmutableHashSet.Create(
        "DisposeAsync"); // IAsyncDisposable's contract takes no parameters at all.

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;

        // A method overriding a base member can't unilaterally change its
        // signature — the base (or the interface it implements elsewhere)
        // is where this should be, and would-be enforced there instead.
        if (method.Modifiers.Any(SyntaxKind.OverrideKeyword))
            return;

        if (ExemptMethodNames.Contains(method.Identifier.Text))
            return;

        var symbol = context.SemanticModel.GetDeclaredSymbol(method, context.CancellationToken);
        if (symbol is null || !IsTaskReturning(symbol.ReturnType))
            return;

        var parameters = method.ParameterList.Parameters;
        var lastParam = parameters.Count > 0 ? parameters[parameters.Count - 1] : (ParameterSyntax?)null;
        var lastParamType = lastParam is not null
            ? context.SemanticModel.GetTypeInfo(lastParam.Type!, context.CancellationToken).Type
            : null;

        if (lastParamType?.Name == "CancellationToken")
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, method.Identifier.GetLocation(), symbol.Name));
    }

    private static bool IsTaskReturning(ITypeSymbol returnType)
    {
        if (returnType is not INamedTypeSymbol named)
            return false;

        return named.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks"
            && (named.Name == "Task" || named.Name == "ValueTask");
    }
}
