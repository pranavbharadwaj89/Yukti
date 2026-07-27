using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Yukti.Analyzers;

/// <summary>
/// FR-AUDIT-01 (Volume 1 Part IV §27.2): every command handler inherits
/// AuditableCommandHandler&lt;TCommand,TResult&gt; rather than implementing
/// ICommandHandler&lt;TCommand,TResult&gt; directly, unless explicitly,
/// visibly exempted. A class implementing ICommandHandler&lt;,&gt; through
/// any path other than AuditableCommandHandler is flagged, unless a
/// leading comment on the class contains "audit-exempt:" followed by a
/// reason — the exemption has to be visible in the diff/file, not a
/// config-file suppression nobody reads during review.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoUnauditedCommandHandlerAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "YUKTI002";
    private const string ExemptionMarker = "audit-exempt:";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Command handlers must inherit AuditableCommandHandler",
        messageFormat: "'{0}' implements ICommandHandler<,> without inheriting AuditableCommandHandler<,> — inherit it, or add a leading '// audit-exempt: <reason>' comment",
        category: "Auditing",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "FR-AUDIT-01: every command handler must be audited unless explicitly, visibly exempted.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeClass, SyntaxKind.ClassDeclaration);
    }

    private static void AnalyzeClass(SyntaxNodeAnalysisContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration, context.CancellationToken);
        if (symbol is null || symbol.IsAbstract)
            return;

        var implementsCommandHandler = symbol.AllInterfaces.Any(i => i.OriginalDefinition.Name == "ICommandHandler");
        if (!implementsCommandHandler)
            return;

        if (InheritsAuditableCommandHandler(symbol))
            return;

        if (HasExemptionComment(classDeclaration))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, classDeclaration.Identifier.GetLocation(), symbol.Name));
    }

    private static bool InheritsAuditableCommandHandler(INamedTypeSymbol symbol)
    {
        for (var baseType = symbol.BaseType; baseType is not null; baseType = baseType.BaseType)
            if (baseType.OriginalDefinition.Name == "AuditableCommandHandler")
                return true;
        return false;
    }

    private static bool HasExemptionComment(ClassDeclarationSyntax classDeclaration)
    {
        foreach (var trivia in classDeclaration.GetLeadingTrivia())
        {
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            {
                if (trivia.ToString().IndexOf(ExemptionMarker, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
        }
        return false;
    }
}
