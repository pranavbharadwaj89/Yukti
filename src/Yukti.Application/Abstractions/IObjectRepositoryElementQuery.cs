using Yukti.Domain.SharedKernel;
using Yukti.Domain.UiVocabulary;

namespace Yukti.Application.Abstractions;

/// <summary>
/// Web UI Testing Module Spec, WEB-T2: a RepositoryRef names a page/element pair, never which
/// ObjectRepository artifact it lives in, so resolving one means searching every Object
/// Repository the tenant owns -- a cross-aggregate search, which is why this is a Query
/// (IObjectRepositoryRepository deliberately has no such method, per this file's own
/// "GetById/Save/Delete only" convention). Used by FlowEngine.ResolveLocatorRef (the real
/// Locator a run resolves against) and by the EF-backed IRepositoryEntryExistsChecker
/// implementation Flow.Publish uses (existence is just "FindElement returned non-null").
/// </summary>
public interface IObjectRepositoryElementQuery
{
    Task<Locator?> FindElement(string page, string element, TenantId tenantId, CancellationToken ct);
}
