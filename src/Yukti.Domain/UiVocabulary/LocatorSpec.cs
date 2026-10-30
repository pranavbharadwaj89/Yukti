namespace Yukti.Domain.UiVocabulary;

/// <summary>
/// Web UI Testing Module Spec, WEB-T1: one concrete way to find an element — a strategy plus
/// its value (a CSS selector, an accessible role name, a test id, ...). <see cref="Name"/> is an
/// optional human label (e.g. the accessible name paired with <see cref="LocatorStrategy.Role"/>),
/// never used for matching itself.
/// </summary>
public sealed record LocatorSpec(LocatorStrategy Strategy, string Value, string? Name);
