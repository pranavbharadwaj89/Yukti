using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Yukti.Analyzers;

/// <summary>
/// FR-LOG-01 (Volume 1 Part IV §28.2): every log call must use a structured
/// message template with named placeholders, never a pre-formatted string.
/// Flags interpolated strings ($"...") and '+' concatenation passed as the
/// message-template argument to any Microsoft.Extensions.Logging.ILogger
/// extension method (LogTrace/LogDebug/LogInformation/LogWarning/LogError/
/// LogCritical/Log) or to ILogger.BeginScope. A concatenation/interpolation
/// bakes argument values into the message text itself, defeating the
/// structured fields a log sink needs to query/aggregate on, and is exactly
/// the pattern that made FR-LOG-04's credential/body leak audit necessary
/// in the first place — string-built messages are where secrets get typed
/// inline without anyone noticing.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoInterpolatedLoggerMessageAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "YUKTI001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Log messages must use structured templates",
        messageFormat: "'{0}' must receive a structured message template (const string with {{Placeholders}} and separate arguments), not an interpolated or concatenated string",
        category: "Logging",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "FR-LOG-01: string-interpolated or concatenated log messages are prohibited. Pass a message template plus arguments so the sink can index structured fields.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    private static readonly ImmutableHashSet<string> LoggerMethodNames = ImmutableHashSet.Create(
        "LogTrace", "LogDebug", "LogInformation", "LogWarning", "LogError", "LogCritical", "Log", "BeginScope");

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        var methodName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            _ => null
        };
        if (methodName is null || !LoggerMethodNames.Contains(methodName))
            return;

        var symbol = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol as IMethodSymbol;
        if (symbol is null || !IsILoggerMethod(symbol))
            return;

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (ContainsBuiltMessage(argument.Expression, out var offending))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, offending.GetLocation(), methodName));
                return; // one diagnostic per call site is enough
            }
        }
    }

    private static bool IsILoggerMethod(IMethodSymbol symbol)
    {
        var containingType = symbol.ContainingType;
        if (containingType is null)
            return false;

        // Covers both ILogger.Log/BeginScope instance members and the
        // LoggerExtensions.LogInformation(this ILogger, ...) extension methods.
        if (containingType.Name == "ILogger")
            return true;

        if (symbol.IsExtensionMethod)
        {
            // A reduced extension method (the usual `logger.LogInformation(...)`
            // call syntax) drops the receiver from Parameters — it only shows up
            // via ReceiverType / ReducedFrom.
            if (symbol.ReceiverType?.Name == "ILogger")
                return true;

            var unreduced = symbol.ReducedFrom ?? symbol;
            if (unreduced.Parameters.Length > 0 && unreduced.Parameters[0].Type.Name == "ILogger")
                return true;
        }

        return containingType.AllInterfaces.Any(i => i.Name == "ILogger");
    }

    private static bool ContainsBuiltMessage(ExpressionSyntax expression, out ExpressionSyntax offending)
    {
        switch (expression)
        {
            case InterpolatedStringExpressionSyntax interpolated:
                offending = interpolated;
                return true;

            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression):
                offending = binary;
                return true;

            case ParenthesizedExpressionSyntax parenthesized:
                return ContainsBuiltMessage(parenthesized.Expression, out offending);

            default:
                offending = expression;
                return false;
        }
    }
}
