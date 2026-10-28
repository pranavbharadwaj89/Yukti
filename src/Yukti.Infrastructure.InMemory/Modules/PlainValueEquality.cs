namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// Loose equality over plain CLR values (string/long/double/bool/null/
/// Dictionary/List — the shape JsonPathEvaluator.ToPlainValue and
/// JsonParamNormalizer both produce). Numeric types compare by value
/// regardless of exact CLR type (long 42 == double 42.0), since a JSON
/// number's CLR representation depends on whether System.Text.Json
/// happened to parse it as an integer or a float. Shared by
/// AssertionEvaluator (PathEquals/PathContains) and
/// MinimalJsonSchemaValidator (enum) so both use one comparison rule.
/// </summary>
internal static class PlainValueEquality
{
    public static bool ValuesEqual(object? actual, object? expected)
    {
        if (actual is null || expected is null)
            return actual is null && expected is null;
        if (IsNumeric(actual) && IsNumeric(expected))
            return Convert.ToDouble(actual) == Convert.ToDouble(expected);
        // API Testing Module Spec, API-T7: a number and a string never compare equal just
        // because their text matches — found as a real, already-shipped false-pass bug here:
        // the ToString() fallback below previously made ValuesEqual(1L, "1") return true.
        if (IsNumeric(actual) != IsNumeric(expected))
            return false;
        return actual.Equals(expected) || actual.ToString() == expected.ToString();
    }

    public static bool IsNumeric(object value) => value is long or int or double or float or decimal;
}
