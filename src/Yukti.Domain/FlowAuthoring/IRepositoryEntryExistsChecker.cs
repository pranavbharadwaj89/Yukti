using Yukti.Domain.SharedKernel;

namespace Yukti.Domain.FlowAuthoring;

/// <summary>
/// Web UI Testing Module Spec, WEB-T2: same cross-aggregate port shape as
/// IRequestTemplateExistsChecker -- Flow.Publish needs to know whether a step's referenced
/// Object Repository page/element currently exists, without Flow depending on its repository
/// directly. Optional on Flow.Publish (defaults to null): every existing caller that doesn't
/// care about repository references keeps compiling and behaving identically.
///
/// Deliberately tenant+page+element, not a specific RepositoryId -- the spec's own
/// <c>RepositoryRef</c> domain type carries no RepositoryId (a step names "Checkout page ›
/// submitButton", never which Object Repository artifact that page lives in), so an existence
/// check (and the matching run-time resolution in FlowEngine) searches every Object Repository
/// the tenant owns for a matching page/element pair. Flagged in plan/OPEN-QUESTIONS.md as a real
/// design call made under that ambiguity, not a silent assumption.
/// </summary>
public interface IRepositoryEntryExistsChecker
{
    Task<bool> ExistsAsync(string page, string element, TenantId tenantId, CancellationToken ct);
}
