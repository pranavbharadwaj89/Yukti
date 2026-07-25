using System.Text.RegularExpressions;

namespace Yukti.Domain.FlowAuthoring;

/// <summary>
/// Parsed at construction time (not resolution time) so a Flow can be
/// statically validated at publish time for referencing only variables some
/// prior step actually declares via saveAs — catching a whole class of
/// "flow references a variable that will never exist" error before the
/// Flow is ever run. (Volume 1 Part II §11.7)
/// </summary>
public sealed partial record VariableExpression
{
    public string RawTemplate { get; }
    public IReadOnlyList<string> ReferencedPaths { get; }

    [GeneratedRegex(@"\{\{\s*([a-zA-Z0-9_.]+)\s*\}\}")]
    private static partial Regex TemplatePattern();

    public VariableExpression(string rawTemplate)
    {
        RawTemplate = rawTemplate;
        ReferencedPaths = TemplatePattern()
            .Matches(rawTemplate)
            .Select(m => m.Groups[1].Value)
            .ToList()
            .AsReadOnly();
    }

    public static bool TryParse(string? text, out VariableExpression? expression)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("{{"))
        {
            expression = null;
            return false;
        }
        expression = new VariableExpression(text);
        return expression.ReferencedPaths.Count > 0;
    }
}
