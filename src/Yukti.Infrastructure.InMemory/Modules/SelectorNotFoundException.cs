namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// WebModule's typed signal for "the selector never appeared/became
/// actionable in time" — distinct from Playwright's own generic
/// TimeoutException so the step's failure message always names the
/// selector explicitly (D3-T2), not just Playwright's own wording.
/// </summary>
public sealed class SelectorNotFoundException : Exception
{
    public string Selector { get; }

    public SelectorNotFoundException(string selector, Exception? inner = null)
        : base($"Selector not found: '{selector}'", inner)
    {
        Selector = selector;
    }

    /// <summary>Web UI Testing Module Spec, WEB-T3/T4: a Locator-based resolution failure
    /// carries richer diagnostics (every strategy tried, how many elements each matched) than
    /// the fixed "Selector not found: 'X'" template above can hold — this overload uses the
    /// caller's own detail as the full message instead, so PlaywrightLocatorResolver's
    /// FailureDetail reaches the step's reported error verbatim, not just the entry's name.</summary>
    public SelectorNotFoundException(string selector, string detail)
        : base(detail)
    {
        Selector = selector;
    }
}
