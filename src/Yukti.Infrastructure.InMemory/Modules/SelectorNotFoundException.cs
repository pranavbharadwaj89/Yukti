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

    public SelectorNotFoundException(string selector)
        : base($"Selector not found: '{selector}'")
    {
        Selector = selector;
    }
}
