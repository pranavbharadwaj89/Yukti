namespace Yukti.Domain.UiVocabulary;

/// <summary>
/// Web UI Testing Module Spec, WEB-T1: the shared, engine-neutral way every Web (and later
/// Mobile) action names "the element to act on." A primary strategy plus an ordered list of
/// fallbacks a real engine tries in order if the primary doesn't resolve (WEB-T3) — a fallback
/// succeeding is always a flagged pass, never a silent one, per the spec's own locator-decay
/// design. <see cref="Within"/> chains into a parent element instead of writing complex XPath;
/// <see cref="Nth"/> disambiguates when a locator legitimately matches more than one element.
///
/// No Playwright type may appear anywhere in this file or this namespace (WEB-T1's own hard
/// rule) — see <c>UiVocabularyTests</c> for the assembly-reference proof and the fake-engine
/// design test.
/// </summary>
public sealed record Locator(
    LocatorSpec Primary,
    IReadOnlyList<LocatorSpec> Fallbacks,
    string? Description,
    Locator? Within,
    int? Nth);
