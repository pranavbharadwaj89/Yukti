using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Yukti.Analyzers;

/// <summary>
/// FR-STD-03 (Volume 1 Part VI §40.5): no .Result/.Wait() synchronous-over-
/// async calls anywhere in the codebase — both deadlock-prone on a
/// synchronization-context-bound thread and, more relevantly here,
/// silently defeats every CancellationToken this codebase's other rule
/// (YUKTI003 / FR-STD-02) requires actually threading through: blocking on
/// .Result/.Wait() has no cancellation path at all.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoSyncOverAsyncAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "YUKTI004";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "No synchronous-over-async .Result/.Wait() calls",
        messageFormat: "'{0}' blocks synchronously on a Task — await it instead",
        category: "AsyncStandards",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "FR-STD-03: .Result/.Wait()/.GetAwaiter().GetResult() on a Task/Task<T> are prohibited everywhere in this codebase.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;
        var memberName = memberAccess.Name.Identifier.Text;

        if (memberName != "Result" && memberName != "Wait")
            return;

        var expressionType = context.SemanticModel.GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
        if (!IsTask(expressionType))
            return;

        // "Result" on a Task (non-generic) doesn't compile at all, so any
        // match here is a real Task<T>.Result or Task.Wait()/Task<T>.Wait().
        context.ReportDiagnostic(Diagnostic.Create(Rule, memberAccess.GetLocation(), $"{memberAccess.Expression}.{memberName}"));
    }

    private static bool IsTask(ITypeSymbol? type) =>
        type is INamedTypeSymbol named
        && named.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks"
        && (named.Name == "Task" || named.Name == "ValueTask");
}
