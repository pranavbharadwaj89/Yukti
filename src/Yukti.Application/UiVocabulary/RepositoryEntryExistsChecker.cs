using Yukti.Application.Abstractions;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.SharedKernel;

namespace Yukti.Application.UiVocabulary;

/// <summary>
/// Web UI Testing Module Spec, WEB-T2: the one implementation of IRepositoryEntryExistsChecker —
/// works identically against either IObjectRepositoryElementQuery implementation (in-memory for
/// tests/CLI, EF for the real API), same reasoning as RequestTemplateExistsChecker. Built on the
/// Query port, not a DbContext directly, so Application still never references Infrastructure.
/// </summary>
public sealed class RepositoryEntryExistsChecker : IRepositoryEntryExistsChecker
{
    private readonly IObjectRepositoryElementQuery _elements;

    public RepositoryEntryExistsChecker(IObjectRepositoryElementQuery elements) => _elements = elements;

    public async Task<bool> ExistsAsync(string page, string element, TenantId tenantId, CancellationToken ct) =>
        await _elements.FindElement(page, element, tenantId, ct) is not null;
}
