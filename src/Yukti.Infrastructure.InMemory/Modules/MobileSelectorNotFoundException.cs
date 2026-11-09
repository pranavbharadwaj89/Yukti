namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// Mobile Testing Module Spec, MOB-T4: MobileModule's typed signal for "no locator strategy
/// resolved an element" — the Mobile counterpart to WebModule's own SelectorNotFoundException.
/// Carries AppiumLocatorResolver.Outcome's own rich FailureDetail (every strategy tried, how
/// many elements each matched) as the full message, so it reaches the step's reported error
/// verbatim — never a bare timeout, the same standard MOB-T1's ClassifySessionFailure already
/// established for session creation.
/// </summary>
public sealed class MobileSelectorNotFoundException : Exception
{
    public MobileSelectorNotFoundException(string detail) : base(detail)
    {
    }
}
