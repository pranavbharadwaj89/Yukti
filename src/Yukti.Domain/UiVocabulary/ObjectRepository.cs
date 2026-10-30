using Yukti.Domain.SharedKernel;

namespace Yukti.Domain.UiVocabulary;

/// <summary>
/// Web UI Testing Module Spec, WEB-T3: field follows the same convention as
/// <c>FlowPublishError</c> -- "pages.&lt;page&gt;.&lt;element&gt;" names the entry without
/// re-deriving it from <see cref="Message"/>.
/// </summary>
public sealed record RepositoryPublishError(string Field, string Code, string Message);

public sealed record RepositoryPublishResult(bool Succeeded, IReadOnlyList<RepositoryPublishError> Errors)
{
    public static RepositoryPublishResult Success() => new(true, Array.Empty<RepositoryPublishError>());
    public static RepositoryPublishResult Failure(IEnumerable<RepositoryPublishError> errors) => new(false, errors.ToList());
}

/// <summary>
/// Web UI Testing Module Spec, WEB-T2: the Page Object Model grouping, without the code. Mutable
/// in place with a visible, incrementing <see cref="Version"/> -- deliberately NOT Flow's
/// heavier immutable-version-family model (<c>FamilyId</c> + <c>CreateNewVersion</c>), for the
/// same reason <c>RequestTemplate</c>'s own doc comment gives: nothing downstream needs an old
/// version of a locator to keep resolving, so "editing an entry affects every flow referencing
/// it with zero flow edits" (this spec's own headline gate) can only hold if edits mutate in
/// place and are resolved fresh on every run (<c>FlowEngine.ResolveLocatorRef</c>), never baked
/// into a Flow at publish time. <see cref="RepositoryStatus"/> gates <see cref="Publish"/>'s own
/// validation, not mutability -- an edit after Publish is allowed and does not revert Status.
/// </summary>
public sealed class ObjectRepository : AggregateRoot<RepositoryId>
{
    public TenantId TenantId { get; private set; }
    public string Name { get; private set; }
    public int Version { get; private set; }
    public RepositoryStatus Status { get; private set; }

    private readonly List<RepositoryPage> _pages = new();
    public IReadOnlyList<RepositoryPage> Pages => _pages.AsReadOnly();

    private ObjectRepository(RepositoryId id, TenantId tenantId, string name) : base(id)
    {
        TenantId = tenantId;
        Name = name;
        Version = 1;
        Status = RepositoryStatus.Draft;
    }

    public static ObjectRepository Create(string name, TenantId tenantId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("ObjectRepository name cannot be empty.");
        return new ObjectRepository(RepositoryId.New(), tenantId, name);
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("ObjectRepository name cannot be empty.");
        Name = name;
        Version++;
    }

    /// <summary>Adds the element if its page doesn't exist yet, or replaces it in place if it
    /// does -- never a new page row, since <see cref="RepositoryPage"/>/<see cref="Locator"/>
    /// are plain value records with no identity of their own.</summary>
    public void SetElement(string pageName, string elementName, Locator locator)
    {
        if (string.IsNullOrWhiteSpace(pageName))
            throw new DomainException("A repository page name cannot be empty.");
        if (string.IsNullOrWhiteSpace(elementName))
            throw new DomainException("A repository element name cannot be empty.");

        var pageIndex = _pages.FindIndex(p => p.Name == pageName);
        var elements = pageIndex >= 0
            ? new Dictionary<string, Locator>(_pages[pageIndex].Elements)
            : new Dictionary<string, Locator>();
        elements[elementName] = locator;

        var updatedPage = new RepositoryPage(pageName, elements);
        if (pageIndex >= 0)
            _pages[pageIndex] = updatedPage;
        else
            _pages.Add(updatedPage);

        Version++;
    }

    public void RemoveElement(string pageName, string elementName)
    {
        var pageIndex = _pages.FindIndex(p => p.Name == pageName);
        if (pageIndex < 0 || !_pages[pageIndex].Elements.ContainsKey(elementName))
            return;

        var elements = new Dictionary<string, Locator>(_pages[pageIndex].Elements);
        elements.Remove(elementName);
        _pages[pageIndex] = new RepositoryPage(pageName, elements);
        Version++;
    }

    public Locator? FindElement(string pageName, string elementName) =>
        _pages.FirstOrDefault(p => p.Name == pageName)?.Elements.GetValueOrDefault(elementName);

    /// <summary>WEB-T2: every entry must have a non-empty description (a primary locator always
    /// exists by construction -- <see cref="Locator"/>'s <c>Primary</c> is non-nullable).
    /// Re-runnable: editing a Published repository never needs an "unpublish" step first, and
    /// re-publishing after a fix is just calling this again.</summary>
    public RepositoryPublishResult Publish()
    {
        var errors = new List<RepositoryPublishError>();
        foreach (var page in _pages)
        {
            foreach (var (elementName, locator) in page.Elements)
            {
                if (string.IsNullOrWhiteSpace(locator.Description))
                    errors.Add(new RepositoryPublishError($"pages.{page.Name}.{elementName}", "missing_description",
                        $"Element '{elementName}' on page '{page.Name}' has no description."));
            }
        }

        if (errors.Count > 0)
            return RepositoryPublishResult.Failure(errors);

        Status = RepositoryStatus.Published;
        return RepositoryPublishResult.Success();
    }
}
