namespace Yukti.Domain.UiVocabulary;

/// <summary>
/// Web UI Testing Module Spec, WEB-T1/WEB-T5: Selenium's explicit-wait equivalent, expressed as
/// data instead of a polling loop a caller writes by hand. A real engine's `waitFor` (WEB-T5)
/// evaluates one of these until it becomes true or the step's timeout elapses, then reports
/// which condition never became true and what the actual state was — never a bare "timed out."
/// </summary>
public abstract record WaitCondition;

public sealed record Visible(Locator L) : WaitCondition;
public sealed record Hidden(Locator L) : WaitCondition;
public sealed record Enabled(Locator L) : WaitCondition;
public sealed record Disabled(Locator L) : WaitCondition;
public sealed record TextEquals(Locator L, string Expected) : WaitCondition;
public sealed record TextContains(Locator L, string Fragment) : WaitCondition;
public sealed record AttributeEquals(Locator L, string Attr, string Expected) : WaitCondition;
public sealed record CountEquals(Locator L, int Expected) : WaitCondition;
public sealed record UrlEquals(string Expected) : WaitCondition;
public sealed record UrlContains(string Fragment) : WaitCondition;
public sealed record TitleContains(string Fragment) : WaitCondition;
public sealed record NetworkIdle(TimeSpan Quiet) : WaitCondition;

/// <summary>P1 (WEB-T5) — deliberately the only escape hatch for a caller who thinks they need a
/// fixed sleep. Not implemented by any engine yet; the type exists now so a `waitFor` param
/// schema never needs a breaking change to add it.</summary>
public sealed record CustomPredicate(string Script) : WaitCondition;
