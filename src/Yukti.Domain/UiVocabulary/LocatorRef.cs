namespace Yukti.Domain.UiVocabulary;

/// <summary>
/// Web UI Testing Module Spec, WEB-T2/WEB-T4: what a step param carries where it names "the
/// element to act on" — either a direct <see cref="Locator"/>, or a reference into an
/// <see cref="ObjectRepository"/> resolved against the CURRENT entry on every run (the same
/// "resolved fresh every run, never baked in" rule <c>RequestTemplate</c> resolution already
/// uses), so editing a repository entry reaches every flow that references it with zero flow
/// edits.
/// </summary>
public abstract record LocatorRef;

public sealed record RepositoryRef(string Page, string Element) : LocatorRef;

public sealed record InlineLocator(Locator L) : LocatorRef;
