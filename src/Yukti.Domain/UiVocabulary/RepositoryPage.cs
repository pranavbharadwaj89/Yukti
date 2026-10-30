namespace Yukti.Domain.UiVocabulary;

/// <summary>Web UI Testing Module Spec, WEB-T2: one page/screen's named elements. A plain value
/// record with no identity of its own -- <see cref="ObjectRepository"/> owns replacing it
/// wholesale when one of its elements changes.</summary>
public sealed record RepositoryPage(string Name, IReadOnlyDictionary<string, Locator> Elements);
